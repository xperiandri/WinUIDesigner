// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.VisualStudio.DesignTools.Utility;
using WinUIDesigner.Platform;

namespace WinUIDesigner.DesignerHost;

/// <summary>
/// A project as <see cref="WinUIProjectRequirements"/> sees it: the platform identity of its active target and the
/// WinUI runtime it references.
/// </summary>
internal interface IWinUIRequirementsProject
{
    /// <summary>
    /// Gets the platform identity of the project's active target.
    /// </summary>
    PlatformIdentifier? PlatformIdentifier { get; }

    /// <summary>
    /// Gets the file version of the Microsoft.WinUI.dll the project references, an empty string when the file carries
    /// no version, or <see langword="null"/> when the project has no such reference to verify.
    /// </summary>
    string? WinUIRuntimeFileVersion { get; }
}

/// <summary>
/// Describes what a project must provide for the bundled WinUI surface to preview it.
/// </summary>
/// <remarks>
/// The designer pane asks before it creates the designer and shows the answer in place of the design surface;
/// the shadow-copy worker factory asks again when the surface process is prepared, because that process
/// can belong to another project than the one that owns the open document.
/// </remarks>
internal static class WinUIProjectRequirements
{
    // Microsoft.WindowsAppSDK 2.5.1 resolves to this Microsoft.WinUI.dll version.
    // The assembly is bundled inside WinUISurface.exe for VSIX single-file deployment.
    public const string VerifiedWinUIRuntimeFileVersion = "3.0.0.2609";

    public const string VerifiedWindowsAppSdkVersion = "2.5.1";

    private const string _WinUIRuntimeFileName = "Microsoft.WinUI.dll";

    private static readonly ConcurrentDictionary<string, KeyValuePair<long, string>> _FileVersions = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Explains why the project cannot be previewed, or returns <see langword="null"/> when it can.
    /// </summary>
    /// <param name="platformIdentifier">The platform identity of the project.</param>
    /// <param name="referencePaths">The resolved reference paths of the project.</param>
    public static string? GetUnsupportedReason(PlatformIdentifier? platformIdentifier, IEnumerable<string?>? referencePaths)
    {
        return GetUnsupportedReason(platformIdentifier, GetWinUIRuntimeFileVersion(referencePaths));
    }

    /// <summary>
    /// Explains why the project cannot be previewed, or returns <see langword="null"/> when it can.
    /// </summary>
    /// <param name="platformIdentifier">The platform identity of the project.</param>
    /// <param name="winUIRuntimeFileVersion">
    /// The file version of the Microsoft.WinUI.dll the project references, an empty string when the file
    /// carries no version, or <see langword="null"/> when the project has no such reference to verify.
    /// </param>
    public static string? GetUnsupportedReason(PlatformIdentifier? platformIdentifier, string? winUIRuntimeFileVersion)
    {
        // The project does not target .NET (Core).
        if (!string.Equals(platformIdentifier?.TargetFrameworkIdentifier, ".NETCoreApp", StringComparison.Ordinal))
        {
            return "WinUI Designer currently supports .NETCoreApp projects only.";
        }

        // The project is not a managed one.
        if (!string.Equals(platformIdentifier?.TargetRuntime, "Managed", StringComparison.OrdinalIgnoreCase))
        {
            return $"WinUI Designer requires a managed project. Runtime: {platformIdentifier?.TargetRuntime}.";
        }

        // The project references another Microsoft.WinUI.dll than the one the surface bundles.
        if (winUIRuntimeFileVersion is not null
            && !string.Equals(VerifiedWinUIRuntimeFileVersion, winUIRuntimeFileVersion, StringComparison.Ordinal))
        {
            string actual = winUIRuntimeFileVersion.Length == 0 ? "an unknown version" : winUIRuntimeFileVersion;

            return $"WinUI Designer requires Windows App SDK {VerifiedWindowsAppSdkVersion} (Microsoft.WinUI.dll {VerifiedWinUIRuntimeFileVersion}). The project references Microsoft.WinUI.dll {actual}.";
        }

        return null;
    }

    /// <summary>
    /// Explains why a document of <paramref name="project"/> cannot be previewed in the surface process it would use,
    /// or returns <see langword="null"/> when it can.
    /// </summary>
    /// <param name="project">The project that contains the document.</param>
    /// <param name="application">
    /// The application that Visual Studio finds for the surface process of <paramref name="project"/> when that is
    /// another project, otherwise <see langword="null"/>.
    /// </param>
    /// <remarks>
    /// A library is previewed in the surface process of the application that references it when that application can
    /// launch one, and that process runs the application's WinUI runtime. The library binds to it there, because
    /// Microsoft.WinUI.dll keeps its assembly version across Windows App SDK versions, so the version the library
    /// itself references does not matter then.
    /// </remarks>
    public static string? GetUnsupportedReason(IWinUIRequirementsProject project, IWinUIRequirementsProject? application)
    {
        // A library in the surface process of its application: only its own target framework and runtime count.
        if (application is not null
            && WinUIPlatformSupport.IsHosted(application.PlatformIdentifier)
            && GetUnsupportedReason(application.PlatformIdentifier, application.WinUIRuntimeFileVersion) is null)
        {
            return GetUnsupportedReason(project.PlatformIdentifier, (string?)null);
        }

        // Any other project starts a surface process of its own, which runs the WinUI runtime the project references.
        return GetUnsupportedReason(project.PlatformIdentifier, project.WinUIRuntimeFileVersion);
    }

    /// <summary>
    /// Finds the Microsoft.WinUI.dll reference among the project's reference paths.
    /// </summary>
    public static string? FindWinUIRuntimePath(IEnumerable<string?>? referencePaths)
    {
        if (referencePaths is null)
        {
            return null;
        }

        foreach (string? referencePath in referencePaths)
        {
            if (!string.IsNullOrEmpty(referencePath)
                && string.Equals(Path.GetFileName(referencePath), _WinUIRuntimeFileName, StringComparison.OrdinalIgnoreCase))
            {
                return referencePath;
            }
        }

        return null;
    }

    /// <summary>
    /// Reads the file version of the Microsoft.WinUI.dll among the project's reference paths, with the meaning of
    /// <see cref="IWinUIRequirementsProject.WinUIRuntimeFileVersion"/>.
    /// </summary>
    public static string? GetWinUIRuntimeFileVersion(IEnumerable<string?>? referencePaths)
    {
        // The project has no Microsoft.WinUI.dll reference, or the file is not there: nothing to verify.
        string? path = FindWinUIRuntimePath(referencePaths);
        if (path is null || !File.Exists(path))
        {
            return null;
        }

        // An unchanged file: the designer pane repeats this question while the document is open.
        long stamp = File.GetLastWriteTimeUtc(path).Ticks;
        if (_FileVersions.TryGetValue(path, out KeyValuePair<long, string> cached) && cached.Key == stamp)
        {
            return cached.Value;
        }

        // A new or changed file: read its version once.
        string version = FileVersionInfo.GetVersionInfo(path).FileVersion ?? string.Empty;
        _FileVersions[path] = new KeyValuePair<long, string>(stamp, version);

        return version;
    }
}
