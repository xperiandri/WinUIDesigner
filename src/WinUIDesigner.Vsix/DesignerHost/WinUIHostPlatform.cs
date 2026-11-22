// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using Microsoft.VisualStudio.DesignTools.DesignerContract;
using Microsoft.VisualStudio.DesignTools.DesignerHost.ShadowCopy;
using Microsoft.VisualStudio.DesignTools.Utility;
using Microsoft.VisualStudio.DesignTools.WpfDesignerHost;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;

namespace WinUIDesigner.DesignerHost;

// WinUI and WPF both host desktop XAML surfaces in isolated processes, so WinUI can
// reuse WPF's process-lifetime and IPC services. UWP uses its DesignerAppView-based
// hosting flow and therefore has a separate host implementation.
public sealed class WinUIHostPlatform : WpfHostPlatform
{
    // Unsupported projects already written to the log; the designer pane repeats the question.
    private static readonly ConcurrentDictionary<string, byte> _ReportedProjects = new();

    protected override IShadowCopyWorkerFactory ShadowCopyWorkerFactory { get; } = new WinUIShadowCopyWorkerFactory();

    public WinUIHostPlatform(IServiceProvider serviceProvider, PlatformIdentifier platformIdentifier)
        : base(serviceProvider, platformIdentifier)
    {
        WinUIDesignerLogger.LogInformation("Host", $"WinUIHostPlatform instantiated for '{platformIdentifier.Identifier}' (XamlRuntime={platformIdentifier.XamlRuntime}).");
    }

    // The designer pane asks this before it creates the designer and shows the summary and details in place
    // of the design surface, so an unsupported project gets an explanation instead of an unhandled exception.
    // It asks only the host of the project's identity: a project whose active target is not Windows has no
    // WinUI host, and Visual Studio shows its own message pane without text for it.
    public override bool IsCompatibleRuntimePlatform(IHostProject hostProject, out string errorSummary, out string errorDetails)
    {
        // The base host platform already rejects the project.
        if (!base.IsCompatibleRuntimePlatform(hostProject, out errorSummary, out errorDetails))
        {
            return false;
        }

        string? reason;
        try
        {
            reason = WinUIProjectRequirements.GetUnsupportedReason(new HostProjectRequirements(hostProject), FindSurfaceApplication(hostProject));
        }
        catch (Exception exception)
        {
            // Do not hide the designer because a reference could not be inspected;
            // the shadow-copy worker factory repeats these checks before the surface starts.
            WinUIDesignerLogger.LogWarning("Host", $"The requirements of '{hostProject.ProjectName}' could not be verified.", exception);

            return true;
        }

        // The project meets the requirements.
        if (reason is null)
        {
            return true;
        }

        // It does not: log that once per project and reason, and show the reason in the pane.
        if (_ReportedProjects.TryAdd($"{hostProject.ProjectName}|{reason}", 0))
        {
            WinUIDesignerLogger.LogWarning("Host", $"Project '{hostProject.ProjectName}' is not supported: {reason}");
        }

        errorSummary = "WinUI Designer cannot preview this project.";
        errorDetails = reason;

        return false;
    }

    // The application whose surface process previews the documents of the project when that is another project.
    // ProjectContextBase looks for it the same way when it creates the surface process of the project.
    private static IWinUIRequirementsProject? FindSurfaceApplication(IHostProject hostProject)
    {
        IHostProject? application = hostProject.IsValid ? hostProject.FindApplicationProject() : null;

        return application is null || application.ProjectGuid == hostProject.ProjectGuid ? null : new HostProjectRequirements(application);
    }

    protected override ISurfaceProcess ActivateSurface(IServiceProvider serviceProvider, Guid surfaceProcessId, string path, string tapPath, IPipeDataBridge dataBridge, bool inhibitStartupWatsons, CancellationToken cancelToken)
    {
        cancelToken.ThrowIfCancellationRequested();

        using Process currentProcess = Process.GetCurrentProcess();
        var initializationData = dataBridge.Serialize(currentProcess.Id);

        WinUIDesignerLogger.LogInformation("Host", $"Surface activation reached: '{path}'.");

        Process surfaceProcess = StartSurfaceProcess(path, tapPath, initializationData);

        if (cancelToken.IsCancellationRequested)
        {
            try
            {
                surfaceProcess.Kill();
            }
            finally
            {
                surfaceProcess.Dispose();
            }

            cancelToken.ThrowIfCancellationRequested();
        }

        WinUIDesignerLogger.LogInformation("Host", $"WinUISurface.exe started (PID={surfaceProcess.Id}); pipe initialization data passed.");

        return new Win32SurfaceProcess(surfaceProcess, surfaceProcessId);
    }

    private static Process StartSurfaceProcess(string path, string tapPath, string initializationData)
    {
        using Process currentProcess = Process.GetCurrentProcess();

        var ideDirectory = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName);
        var commonDirectory = ideDirectory is null ? null : Directory.GetParent(ideDirectory)?.FullName;
        var installRoot = commonDirectory is null ? null : Directory.GetParent(commonDirectory)?.FullName;

        installRoot = installRoot is not null && Directory.Exists(Path.Combine(installRoot, "Common7", "IDE", "PrivateAssemblies"))
            ? installRoot
            : throw new FileNotFoundException("The Visual Studio installation root did not found.");

        // Keep this argument order in sync with WinUIDesignerSurface.exe!Program.Main:
        // 1. VS host PID
        // 2. Path to the TAP dll
        // 3. serialized pipe initialization data
        // 4. inherited DPI awareness context
        // 5. Path to the VS install root for private contract DLL resolution
        // 6. verbose logging setting
        var arguments = string.Format(
            CultureInfo.InvariantCulture,
            "{0} {1} {2} {3} {4} {5}",
            currentProcess.Id,
            ShellEscape(tapPath),
            ShellEscape(initializationData),
            DpiHelper.GetProcessDpiAwarenessContext().ToInt64(),
            ShellEscape(installRoot),
            WinUIDesignerLogger.IsVerboseLoggingEnabled);

        var process = new Process()
        {
            StartInfo = new(path, arguments)
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
            EnableRaisingEvents = true,
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                WinUIDesignerLogger.LogWarning("Host", $"WinUISurface stderr: {e.Data}");
            }
        };
        process.Exited += (_, _) =>
        {
            try
            {
                if (process.ExitCode != 0)
                {
                    WinUIDesignerLogger.LogError("Host", $"WinUISurface.exe exited (PID={process.Id}, ExitCode={process.ExitCode}).");
                }
            }
            catch (InvalidOperationException)
            {
                WinUIDesignerLogger.LogWarning("Host", "WinUISurface.exe exited before its exit code could be read.");
            }
        };

        if (process.Start())
        {
            process.BeginErrorReadLine();
            return process;
        }

        process.Dispose();

        WinUIDesignerLogger.LogError("Host", $"Failed to start WinUISurface.exe at '{path}'.");

        throw new InvalidProgramException(path);
    }

    // A Visual Studio project as WinUIProjectRequirements sees it.
    private sealed class HostProjectRequirements(IHostProject project) : IWinUIRequirementsProject
    {
        public PlatformIdentifier? PlatformIdentifier => project.PlatformIdentifier;

        public string? WinUIRuntimeFileVersion
        {
            get
            {
                return WinUIProjectRequirements.GetWinUIRuntimeFileVersion(project.References?.Select(reference => reference.Path));
            }
        }
    }
}
