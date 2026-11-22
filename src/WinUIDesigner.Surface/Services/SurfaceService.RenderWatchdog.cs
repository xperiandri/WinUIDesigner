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
    private bool _renderWatchArmed;
    private bool _renderStallReported;

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
        if (_renderWatchArmed || _renderStallReported)
        {
            return;
        }

        _renderWatchArmed = true;
        ArmRenderWatch(exception);
    }

    /// <summary>What the surface was doing, for the log, when the framework reported an error that nothing handled.</summary>
    public string DescribeState()
    {
        return $"Frames of the first island: {RenderFrames.Counter.DescribeFirstIsland()}. Documents: {_surfaces.Count}, shown: {_surfaces.Values.Count(surface => surface.IsHostVisible)}. Latest events:{Environment.NewLine}"
            + string.Join(Environment.NewLine, _breadcrumbs.Snapshot().Select(entry => "  " + entry));
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
                if (RenderStall.ShouldLookAgain(shownIds.Length, _surfaces.Values.Any(surface => surface.IsFrozen)))
                {
                    ArmRenderWatch(exception);
                    return;
                }

                // A render that the framework does not complete, even once its thread is free again: it has stopped
                // drawing. The frames meanwhile only go into the log: a thread that is busy for a while draws none
                // either, and they are counted only now, because counting them makes the framework draw.
                FrameCounter frames = RenderFrames.Counter;
                int framesAtStart = frames.Count;
                bool counted = frames.StartWatching();
                bool canDraw;
                try
                {
                    canDraw = await _surfaces[shownIds[0]].CanDrawAsync(
                        TimeSpan.FromSeconds(_RenderProbeSeconds),
                        TimeSpan.FromMilliseconds(_RenderProbeGraceMilliseconds)
                    );
                }
                finally
                {
                    frames.StopWatching();
                }

                _renderWatchArmed = false;
                string framesWhileRendering = counted ? $"{frames.Count - framesAtStart} frames" : "frames not counted";
                if (canDraw)
                {
                    WinUIDesignerLogger.LogInformation("Surface", $"The surface still draws after the error that the framework reported ({framesWhileRendering} while it rendered a pixel).");
                }
                else
                {
                    ReportRenderStall(exception, shownIds, framesWhileRendering);
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

    private void ReportRenderStall(Exception exception, int[] shownDocumentIds, string framesWhileRendering)
    {
        _renderStallReported = true;

        // Nothing in this process draws again: there is nothing left to count.
        RenderFrames.Counter.Stop();

        // The text is also how the extension knows that nothing in this process draws any more, and restarts the process.
        const string Message = RenderStallReport.Message;
        WinUIDesignerLogger.LogError("Surface", $"{Message} A render of one pixel did not complete ({framesWhileRendering} meanwhile). {DescribeState()}", exception);

        // Visual Studio shows the error on the view of the document that it names, and ignores one that names none.
        foreach (int documentId in shownDocumentIds)
        {
            _protocolHandler.PostMessage((int)DesignerMessageId.UnhandledException, new UnhandledExceptionResponse()
            {
                DocumentId = documentId,
                Message = Message,
                CallStack = exception.ToString(),
                IsArtboardException = true,
            });
        }
    }
}
