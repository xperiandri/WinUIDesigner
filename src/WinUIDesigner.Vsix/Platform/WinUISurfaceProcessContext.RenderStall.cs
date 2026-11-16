// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Threading;
using System.Windows.Threading;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.Networking;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.TapOM;
using WinUIDesigner.Protocol;
using WinUIDesigner.Recovery;

namespace WinUIDesigner.Platform;

// The XAML framework of a surface process can stop drawing after an error that it reports to nobody (the surface notices,
// and says so with an unhandled-exception message). Nothing in such a process draws again, and Visual Studio's own
// "reload the designer" reloads the documents inside the same process, so the surface is restarted instead, the way
// Visual Studio restarts it after a change of the project.
internal sealed partial class WinUISurfaceProcessContext
{
    // A surface that stops drawing again and again, for a reason that a restart does not remove, is left alone.
    private const int _MaximumRenderStallRestarts = 3;
    private static readonly TimeSpan _RenderStallRestartWindow = TimeSpan.FromMinutes(2);

    private readonly RestartLimiter _renderStallRestarts = new(_MaximumRenderStallRestarts, _RenderStallRestartWindow);
    private readonly Dispatcher _renderStallDispatcher = Dispatcher.CurrentDispatcher;
    private bool _isDisposedWhileWatchingRenderStalls;

    // Called for the protocol of every surface process that Visual Studio starts: the observers go away with the process.
    private void ObserveRenderStalls(IProtocolHandler protocolHandler)
    {
        // The surface reports a stall once for every document that it shows; the process is restarted once.
        int reported = 0;

        try
        {
            // Visual Studio observes the same message to show the error on the view; this observer only listens.
            _ = protocolHandler.RegisterMessageObserver<UnhandledExceptionResponse>((int)DesignerMessageId.UnhandledException, response =>
            {
                if (RenderStallReport.IsReport(response?.Message) && Interlocked.Exchange(ref reported, 1) == 0)
                {
                    // On the thread of the protocol: the restart is Visual Studio's business on its own thread.
                    _ = _renderStallDispatcher.InvokeAsync(RestartAfterRenderStall);
                }
            });
        }
        catch (Exception exception)
        {
            // Without it a surface that has stopped drawing is reloaded by hand, as it was before.
            WinUIDesignerLogger.LogWarning("Platform", "The extension cannot watch for a surface that has stopped drawing.", exception);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // A restart requested after this point would leave the base class's timer running forever.
            _isDisposedWhileWatchingRenderStalls = true;
        }

        base.Dispose(disposing);
    }

    private void RestartAfterRenderStall()
    {
        if (_isDisposedWhileWatchingRenderStalls)
        {
            return;
        }

        string project = ProjectContext?.ProjectName ?? "a project";
        if (!_renderStallRestarts.TryAcquire(DateTime.UtcNow))
        {
            WinUIDesignerLogger.LogWarning(
                "Platform",
                $"The surface of '{project}' has stopped drawing {_MaximumRenderStallRestarts + 1} times in {_RenderStallRestartWindow.TotalMinutes:0} minutes. "
                    + "It is not restarted again; reload the designer when the cause is gone.");
            return;
        }

        WinUIDesignerLogger.LogWarning("Platform", $"The surface of '{project}' has stopped drawing; its process is restarted.");
        RestartAfter(MediumDelay);
    }
}
