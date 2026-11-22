// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using Microsoft.VisualStudio.DesignTools.RuntimeHost.Networking;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.TapOM;
using System;
using System.Linq;
using WinUIDesigner.Recovery;

namespace WinUIDesigner.Surface.Services;

internal sealed partial class SurfaceService
{
    private const int _RenderWatchSeconds = 3;
    private const int _RenderProbeSeconds = 2;
    private const int _RenderProbeGraceMilliseconds = 500;

    private readonly Breadcrumbs _breadcrumbs = new(24);
    private readonly RenderStallReports _renderStall = new();
    private bool _renderWatchArmed;

    // App.xaml as the surface last built it: with the staged files, what every process of the project starts from.
    private string? _applicationXaml;

    // A timer that nothing refers to is collected, and does not fire.
    private DispatcherQueueTimer? _renderWatchTimer;

    private void Breadcrumb(string text) => _breadcrumbs.Add(text);

    /// <summary>
    /// The framework reported an error that nothing handled and that no managed code threw, and the surface let it go
    /// on. The error can leave the framework in a state where it no longer draws anything: the preview then stays empty,
    /// and nothing tells why. Watch whether the framework still draws, and say so when it does not, so that the designer
    /// shows the error and offers to reload it. Call it on the thread of the user interface.
    /// </summary>
    public void OnNativeFrameworkError(Exception exception)
    {
        if (_renderWatchArmed || _renderStall.HasStalled)
        {
            return;
        }

        _renderWatchArmed = true;
        ArmRenderWatch(exception);
    }

    /// <summary>What the surface was doing, for the log, when the framework reported an error that nothing handled.</summary>
    public string DescribeState()
    {
        string events = string.Join(Environment.NewLine, _breadcrumbs.Snapshot().Select(entry => $"  {entry}"));

        return $"Frames of the first island: {RenderFrames.Counter.DescribeFirstIsland()}. Documents: {_surfaces.Count}, shown: {_surfaces.Values.Count(surface => surface.IsHostVisible)}. Latest events:{Environment.NewLine}{events}";
    }

    private void ArmRenderWatch(Exception exception)
    {
        DispatcherQueueTimer timer = _renderWatchTimer = _dispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(_RenderWatchSeconds);
        timer.IsRepeating = false;
        timer.Tick += async (_, _) =>
        {
            try
            {
                // Nothing is shown, so nothing has to be drawn, or a surface is frozen, which suspends rendering on
                // purpose: look again later.
                int[] shownIds = [.. _surfaces.Where(pair => pair.Value.IsHostVisible).Select(pair => pair.Key)];
                if (RenderStall.ShouldLookAgain(shownIds.Length, AnySurfaceFrozen()))
                {
                    ArmRenderWatch(exception);
                    return;
                }

                // A render that the framework does not complete, even once its thread is free again: it has stopped
                // drawing. The frames meanwhile only go into the log: a thread that is busy for a while draws none
                // either, and they are counted only now, because counting them makes the framework draw.
                int probedId = shownIds[0];
                DesignerSurface probed = _surfaces[probedId];
                FrameCounter frames = RenderFrames.Counter;
                int framesAtStart = frames.Count;
                bool counted = frames.StartWatching();
                bool canDraw;
                try
                {
                    canDraw = await probed.CanDrawAsync(
                        TimeSpan.FromSeconds(_RenderProbeSeconds),
                        TimeSpan.FromMilliseconds(_RenderProbeGraceMilliseconds)
                    );
                }
                finally
                {
                    frames.StopWatching();
                }

                string framesWhileRendering = counted ? $"{frames.Count - framesAtStart} frames" : "frames not counted";

                // The render took seconds: what it was asked of can have changed meanwhile.
                switch (RenderStall.AfterProbe(canDraw, IsStillShown(probedId, probed), AnySurfaceFrozen()))
                {
                    // The error left the framework drawing.
                    case RenderWatchStep.Draws:
                        _renderWatchArmed = false;
                        WinUIDesignerLogger.LogInformation("Surface", $"The surface still draws after the error that the framework reported ({framesWhileRendering} while it rendered a pixel).");
                        break;

                    // No render, but its document was closed, replaced or hidden meanwhile, or a surface was frozen.
                    case RenderWatchStep.LookAgain:
                        WinUIDesignerLogger.LogInformation("Surface", $"A render of one pixel did not complete ({framesWhileRendering} meanwhile), but its document was closed, replaced or hidden, or a surface was frozen, while it ran. The surface looks again later.");
                        ArmRenderWatch(exception);
                        break;

                    // The framework has stopped drawing.
                    case RenderWatchStep.Stalled:
                        _renderWatchArmed = false;
                        ReportRenderStall(exception, framesWhileRendering);
                        break;
                }
            }
            catch (Exception failure)
            {
                _renderWatchArmed = false;
                WinUIDesignerLogger.LogWarning("Surface", "Watching whether the surface still draws failed.", failure);
            }
        };

        timer.Start();
    }

    private bool AnySurfaceFrozen() => _surfaces.Values.Any(surface => surface.IsFrozen);

    // Whether the surface that a render was asked of is still the one of its document, and its island is shown.
    private bool IsStillShown(int documentId, DesignerSurface surface)
    {
        return _surfaces.TryGetValue(documentId, out DesignerSurface? current)
            && ReferenceEquals(current, surface)
            && current.IsHostVisible;
    }

    private void ReportRenderStall(Exception exception, string framesWhileRendering)
    {
        // The text is also how the extension knows that nothing in this process draws any more, and restarts the process;
        // a framework that stopped at the first frame of the process stops the same way in a new one.
        bool stoppedAtFirstFrame = RenderStall.StoppedAtFirstFrame(RenderFrames.Counter.FirstIslandFrames);
        _renderStall.Stalled(RenderStallReport.MessageFor(stoppedAtFirstFrame), exception.ToString());

        // Nothing in this process draws again: there is nothing left to count.
        RenderFrames.Counter.Stop();

        WinUIDesignerLogger.LogError("Surface", $"{_renderStall.Message} A render of one pixel did not complete ({framesWhileRendering} meanwhile). Surface context: {IdentifySurfaceContext()}. {DescribeState()}", exception);

        // The documents that are shown now, not the ones that were shown when the render was asked for: a document that was
        // created or shown while it ran asked before the stall was decided, and was told nothing.
        foreach ((int documentId, DesignerSurface surface) in _surfaces.Where(pair => pair.Value.IsHostVisible).ToArray())
        {
            TellRenderStall(documentId, surface);
        }
    }

    /// <summary>
    /// Tells Visual Studio that the surface of the document is not drawn, once the framework of the process has stopped
    /// drawing: once for each surface, also for the documents that are created or shown after the report.
    /// </summary>
    private void TellRenderStall(int documentId, DesignerSurface surface)
    {
        if (!_renderStall.ShouldTell(surface))
        {
            return;
        }

        // Telling Visual Studio is best effort: a failure must not fail the creation or the position of the document that
        // asked, and the next call tells the surface again.
        try
        {
            // Visual Studio shows the error on the view of the document that it names, and ignores one that names none. The
            // context is named anew each time: a report after a change of App.xaml or of the staged files may restart again.
            _protocolHandler.PostMessage((int)DesignerMessageId.UnhandledException, new UnhandledExceptionResponse()
            {
                DocumentId = documentId,
                Message = _renderStall.Message,
                CallStack = RenderStallReport.WithContext(IdentifySurfaceContext(), _renderStall.Details),
                IsArtboardException = true,
            });
        }
        catch (Exception failure)
        {
            _renderStall.NotTold(surface);
            WinUIDesignerLogger.LogWarning("Surface", $"The report that the surface of document {documentId} is not drawn could not be sent.", failure);
        }
    }

    private string IdentifySurfaceContext()
    {
        string directory = Program.ContentDirectory;

        return SurfaceContext.Identify(directory, SurfaceContext.DescribeStagedFiles(directory), _applicationXaml);
    }
}
