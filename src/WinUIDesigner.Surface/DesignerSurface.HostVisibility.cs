// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using WinUIDesigner.Surface.Common;

namespace WinUIDesigner.Surface;

internal sealed partial class DesignerSurface
{
    // How often the state of a host that the framework says is hidden is compared with that of its window.
    private static readonly TimeSpan _HostVisibilityInterval = TimeSpan.FromMilliseconds(250);

    // Hiding the window and showing it again is tried this many times before the surface leaves the host as it is.
    private const int _MaximumHostVisibilityRepairs = 3;

    // Created the first time that the framework says that the host is hidden, and disposed with the surface.
    private HostVisibilityWatch? _hostVisibilityWatch;

    /// <summary>
    /// The framework learns that the host of an island is shown or hidden from the messages that the window of the island
    /// gets when Visual Studio hides or shows the designer of a document (a tab that is left, a tab that is opened), and
    /// does not draw while it thinks that the host is hidden. It has been seen to keep that belief after the window was
    /// shown again: the window and every parent of it visible, <c>XamlRoot.IsHostVisible</c> false, the controls laid out and
    /// reachable through UI Automation, and nothing drawn, until the window is hidden and shown once more. So while the
    /// framework says that the host is hidden, the window is looked at four times a second, and when it is visible it is
    /// hidden and shown again, which tells the framework. A window that really is hidden, or whose top-level window is
    /// minimized, is left alone: the message that shows it comes with the show. When three attempts do not help, the window
    /// is only looked at: a hide and a show by Visual Studio (another tab and back), or a new position or size of the
    /// surface, start the attempts again.
    /// </summary>
    /// <param name="startAgain">
    /// Visual Studio has just placed the surface (<see cref="SetSurfacePosition"/>: created, moved or resized): earlier attempts do not count.
    /// </param>
    private void WatchHostVisibility(bool startAgain = false)
    {
        if (_disposed || _xamlSource is null)
        {
            return;
        }

        if (_viewportRoot.XamlRoot is not { IsHostVisible: false })
        {
            _hostVisibilityWatch?.Stop();
            return;
        }

        _hostVisibilityWatch ??= new HostVisibilityWatch(DispatcherQueue.GetForCurrentThread().CreateTimer(), _HostVisibilityInterval, _MaximumHostVisibilityRepairs, OnHostVisibilityTimerTick);
        _hostVisibilityWatch.Start(startAgain);
    }

    private void OnHostVisibilityTimerTick(HostVisibilityWatch watch)
    {
        if (_disposed || _xamlSource is null || _viewportRoot.XamlRoot is not { } root || root.IsHostVisible)
        {
            watch.Stop();
            return;
        }

        IntPtr bridge = Win32Interop.GetWindowFromWindowId(_xamlSource.SiteBridge.WindowId);
        bool windowIsVisible = bridge != IntPtr.Zero && NativeMethods.IsWindowVisible(bridge);
        bool topLevelWindowIsMinimized = windowIsVisible && NativeMethods.IsIconic(NativeMethods.GetAncestor(bridge, NativeMethods.GA_ROOT));

        switch (watch.Decide(root.IsHostVisible, windowIsVisible, topLevelWindowIsMinimized))
        {
            case HostVisibilityAction.ShowAgain:
                WinUIDesignerLogger.LogInformation("Surface", $"The window of the island is visible and the framework says that its host is hidden, so nothing is drawn: the window is hidden and shown again (attempt {watch.Attempts} of {_MaximumHostVisibilityRepairs}).");
                _ = NativeMethods.ShowWindow(bridge, NativeMethods.SW_HIDE);
                _ = NativeMethods.ShowWindow(bridge, NativeMethods.SW_SHOW);
                break;

            case HostVisibilityAction.GiveUp:
                WinUIDesignerLogger.LogWarning("Surface", $"The framework still says that the host of the island is hidden while its window is visible, after the window was hidden and shown again {watch.Attempts} times; no more attempts until Visual Studio hides and shows the surface or moves or resizes it.");
                break;
        }
    }
}
