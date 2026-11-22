// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using WinUIDesigner.DesignerHost;

namespace WinUIDesigner.Platform;

/// <summary>
/// A project context as <see cref="WinUISurfaceLaunch"/> sees it.
/// </summary>
internal interface ISurfaceLaunchContext : IWinUIRequirementsProject
{
    /// <summary>
    /// Gets the name of the project, for messages.
    /// </summary>
    string? ProjectName { get; }

    /// <summary>
    /// Gets whether the context has been closed.
    /// </summary>
    bool IsDisposed { get; }

    /// <summary>
    /// Gets whether the context is attached to its project in Visual Studio.
    /// </summary>
    bool HasHostProject { get; }

    /// <summary>
    /// Gets whether the context has its designer metadata.
    /// </summary>
    bool HasMetadata { get; }

    /// <summary>
    /// Gets whether the designer metadata has its platform part, which names the control assemblies to shadow-copy.
    /// </summary>
    bool HasPlatformMetadata { get; }
}

/// <summary>
/// Decides whether a WinUI design surface can be launched through a project context.
/// </summary>
/// <remarks>
/// Visual Studio launches the surface through the project context, and that implementation hands the project, the
/// process host and the platform part of the designer metadata on without checking them. A context that lacks one of
/// them would fail there with a <see cref="System.NullReferenceException"/> instead of a reason.
/// </remarks>
internal static class WinUISurfaceLaunch
{
    /// <summary>
    /// Explains why a design surface cannot be launched through <paramref name="context"/>, or returns
    /// <see langword="null"/> when it can.
    /// </summary>
    public static string? GetBlocker(ISurfaceLaunchContext? context)
    {
        // There is no project context at all.
        if (context is null)
        {
            return "The design surface has no project context.";
        }

        // The context was closed while its surface was being prepared.
        if (context.IsDisposed)
        {
            return $"The designer context of project '{context.ProjectName}' was closed before its design surface started. Close and reopen the XAML document.";
        }

        // The context is not attached to its project, which Visual Studio hands to the process host.
        if (!context.HasHostProject)
        {
            return "The designer context has no project, so its design surface cannot be started. Close and reopen the XAML document.";
        }

        // The active target changed to one without a WinUI process host after the context was created.
        if (!WinUIPlatformSupport.IsHosted(context.PlatformIdentifier))
        {
            return $"Project '{context.ProjectName}' cannot start a WinUI design surface: no WinUI process host matches '{context.PlatformIdentifier?.Identifier}'. "
                + "Make a Windows target framework (net*-windows10.0.*) the active target of this project.";
        }

        // The designer metadata of the project is missing.
        if (!context.HasMetadata)
        {
            return $"Project '{context.ProjectName}' has no designer metadata, so its design surface cannot be started. Close and reopen the XAML document.";
        }

        // The designer metadata has no platform part, which names the control assemblies to shadow-copy.
        if (!context.HasPlatformMetadata)
        {
            return $"Project '{context.ProjectName}' has no designer platform metadata, so its design surface cannot be started. Close and reopen the XAML document.";
        }

        return null;
    }

    /// <summary>
    /// Explains why a library cannot use the surface process of <paramref name="application"/>, or returns
    /// <see langword="null"/> when it can.
    /// </summary>
    /// <remarks>
    /// That process runs the output and the WinUI runtime of the application, so the application must also meet
    /// <see cref="WinUIProjectRequirements"/> itself. Otherwise the shadow-copy worker factory refuses to prepare the
    /// process, although the library could be previewed in a surface process of its own.
    /// </remarks>
    public static string? GetSharingBlocker(ISurfaceLaunchContext? application)
    {
        // GetBlocker reports a missing application, so the requirements are only checked for an existing one.
        return GetBlocker(application)
            ?? WinUIProjectRequirements.GetUnsupportedReason(application!.PlatformIdentifier, application.WinUIRuntimeFileVersion);
    }
}
