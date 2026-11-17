// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

namespace WinUIDesigner.Surface.Common;

/// <summary>What the surface does about a framework that says the host of an island is hidden.</summary>
internal enum HostVisibilityAction
{
    /// <summary>Nothing is wrong, or nothing can be put right now.</summary>
    Nothing,

    /// <summary>The window is visible and the framework does not know it: hide the window and show it again.</summary>
    ShowAgain,

    /// <summary>Showing the window again did not help often enough: leave the host as it is.</summary>
    GiveUp,
}

/// <summary>
/// Decides what to do when the framework (<c>XamlRoot.IsHostVisible</c>) and the window of the island disagree about
/// whether the island is shown. See <c>DesignerSurface.WatchHostVisibility</c>.
/// </summary>
internal static class HostVisibilityPolicy
{
    public static HostVisibilityAction Decide(bool frameworkSaysHostIsVisible, bool windowIsVisible, bool topLevelWindowIsMinimized, int attemptsSoFar, int maximumAttempts)
    {
        // The framework and the window agree, or Visual Studio has hidden the window (the message that shows it comes with
        // the show), or nothing is on screen anyway (a minimized window is hidden for the framework in other ways).
        if (frameworkSaysHostIsVisible || !windowIsVisible || topLevelWindowIsMinimized)
        {
            return HostVisibilityAction.Nothing;
        }

        return attemptsSoFar >= maximumAttempts ? HostVisibilityAction.GiveUp : HostVisibilityAction.ShowAgain;
    }
}
