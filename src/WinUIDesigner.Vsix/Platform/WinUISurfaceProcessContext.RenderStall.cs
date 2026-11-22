// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
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

    private readonly Dispatcher _renderStallDispatcher = Dispatcher.CurrentDispatcher;
    private bool _isDisposedWhileWatchingRenderStalls;

    // Called for the protocol of every surface process that Visual Studio starts: the observers go away with the process.
    private void ObserveRenderStalls(IProtocolHandler protocolHandler)
    {
        // The surface reports a stall for every document that it shows, also after its first report; the process is
        // restarted once.
        var reports = new RenderStallProcessReports();

        try
        {
            // Visual Studio observes the same message to show the error on the view; this observer only listens.
            _ = protocolHandler.RegisterMessageObserver<UnhandledExceptionResponse>((int)DesignerMessageId.UnhandledException, response =>
            {
                string? message = response?.Message;
                if (RenderStallReport.IsReport(message))
                {
                    // On the thread of the protocol: the restart is Visual Studio's business on its own thread.
                    string? callStack = response?.CallStack;
                    _ = _renderStallDispatcher.InvokeAsync(() => RestartAfterRenderStall(reports, message, callStack));
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

    private void RestartAfterRenderStall(RenderStallProcessReports reports, string? message, string? callStack)
    {
        // Visual Studio makes a new context for the project when its designers are opened again; the policy outlives it.
        RenderStallRestartPolicy policy = RenderStallRestartPolicy.ForProject(ProjectContext?.ProjectPath, _MaximumRenderStallRestarts, _RenderStallRestartWindow);
        string project = ProjectContext?.ProjectName ?? "a project";
        switch (reports.Decide(policy, message, callStack, DateTime.UtcNow, _isDisposedWhileWatchingRenderStalls))
        {
            // The first report of the process that the policy allows.
            case RenderStallRestart.Restart:
                WinUIDesignerLogger.LogWarning("Platform", $"The surface of '{project}' has stopped drawing; its process is restarted.");
                RestartAfter(MediumDelay);
                break;

            // A new process stopped at its first frame again, with the same staged files and App.xaml.
            case RenderStallRestart.StopsAtFirstFrame:
                WinUIDesignerLogger.LogWarning("Platform", $"The surface of '{project}' has stopped drawing at its first frame again after a restart. The cause is probably in what the document or App.xaml loads; the surface is not restarted for it until App.xaml or the build of the project changes.");
                break;

            // Restarted too often: the process stays, and the stall that it reports for a document loaded later is
            // decided again.
            case RenderStallRestart.LimitReached:
                WinUIDesignerLogger.LogWarning("Platform", $"The surface of '{project}' has stopped drawing again after {_MaximumRenderStallRestarts} restarts in {_RenderStallRestartWindow.TotalMinutes:0} minutes, and is not restarted now. Reloading the designer restarts it once those restarts are {_RenderStallRestartWindow.TotalMinutes:0} minutes old, and so does a rebuild of the project.");
                break;

            // Nothing to do, or nothing new to say: the process is restarted already, or the refusal was logged.
            default:
                break;
        }
    }
}
