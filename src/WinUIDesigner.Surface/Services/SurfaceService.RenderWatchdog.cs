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
    /// and nothing tells why. Watch whether frames are still drawn, and say so when they are not, so that the designer
    /// shows the error and offers to reload it. Call it on the thread of the user interface.
    /// </summary>
    public void OnNativeFrameworkError(Exception exception)
    {
        RenderFrames.EnsureStarted();

        if (_renderWatchArmed || _renderStallReported)
        {
            return;
        }

        _renderWatchArmed = true;
        ArmRenderWatch(exception, RenderFrames.Count);
    }

    /// <summary>What the surface was doing, for the log, when the framework reported an error that nothing handled.</summary>
    public string DescribeState()
    {
        return $"Frames drawn so far: {RenderFrames.Count}. Documents: {_surfaces.Count}, shown: {_surfaces.Values.Count(surface => surface.IsHostVisible)}. Latest events:{Environment.NewLine}"
            + string.Join(Environment.NewLine, _breadcrumbs.Snapshot().Select(entry => "  " + entry));
    }

    private void ArmRenderWatch(Exception exception, int framesAtStart)
    {
        DispatcherQueueTimer timer = _renderWatchTimer = _dispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(_RenderWatchSeconds);
        timer.IsRepeating = false;
        timer.Tick += async (_, _) =>
        {
            try
            {
                int frames = RenderFrames.Count;

                // Nothing is shown, so nothing has to be drawn, or a surface is frozen, which suspends rendering on
                // purpose: look again, counting from now.
                int[] shownIds = [.. _surfaces.Where(pair => pair.Value.IsHostVisible).Select(pair => pair.Key)];
                if (RenderStall.ShouldLookAgain(shownIds.Length, _surfaces.Values.Any(surface => surface.IsFrozen)))
                {
                    ArmRenderWatch(exception, frames);
                    return;
                }

                // A render that the framework does not complete, even once its thread is free again: it has stopped
                // drawing. The frames only go into the log: a thread that is busy for a while draws none either.
                bool canDraw = await _surfaces[shownIds[0]].CanDrawAsync(
                    TimeSpan.FromSeconds(_RenderProbeSeconds),
                    TimeSpan.FromMilliseconds(_RenderProbeGraceMilliseconds)
                );
                _renderWatchArmed = false;

                if (canDraw)
                {
                    WinUIDesignerLogger.LogInformation("Surface", $"The surface still draws after the error that the framework reported ({frames - framesAtStart} frames in {_RenderWatchSeconds} s).");
                }
                else
                {
                    ReportRenderStall(exception, shownIds);
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

    private void ReportRenderStall(Exception exception, int[] shownDocumentIds)
    {
        _renderStallReported = true;

        // The text is also how the extension knows that nothing in this process draws any more, and restarts the process.
        const string Message = RenderStallReport.Message;
        WinUIDesignerLogger.LogError("Surface", $"{Message} {DescribeState()}", exception);

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
