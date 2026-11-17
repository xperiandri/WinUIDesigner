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

    // A timer that nothing refers to is collected, and does not fire.
    private DispatcherQueueTimer? _hostVisibilityTimer;

    private int _hostVisibilityRepairs;

    /// <summary>
    /// The framework learns that the host of an island is shown or hidden from the messages that the window of the island
    /// gets when Visual Studio hides or shows the designer of a document (a tab that is left, a tab that is opened), and
    /// does not draw while it thinks that the host is hidden. It has been seen to keep that belief after the window was
    /// shown again: the window and every parent of it visible, <c>XamlRoot.IsHostVisible</c> false, the controls laid out and
    /// reachable through UI Automation, and nothing drawn, until the window is hidden and shown once more. So while the
    /// framework says that the host is hidden, the window is looked at four times a second, and when it is visible it is
    /// hidden and shown again, which tells the framework. A window that really is hidden, or whose top-level window is
    /// minimized, is left alone: the message that shows it comes with the show.
    /// </summary>
    /// <param name="startAgain">Visual Studio has just shown or moved the surface: earlier attempts do not count.</param>
    private void WatchHostVisibility(bool startAgain = false)
    {
        if (_disposed || _xamlSource is null)
        {
            return;
        }

        if (_viewportRoot.XamlRoot is not { IsHostVisible: false })
        {
            StopWatchingHostVisibility();
            return;
        }

        if (startAgain)
        {
            _hostVisibilityRepairs = 0;
        }

        DispatcherQueueTimer timer = _hostVisibilityTimer ??= DispatcherQueue.GetForCurrentThread().CreateTimer();
        if (!timer.IsRunning)
        {
            timer.Interval = _HostVisibilityInterval;
            timer.IsRepeating = true;
            timer.Tick -= OnHostVisibilityTimerTick;
            timer.Tick += OnHostVisibilityTimerTick;
            timer.Start();
        }
    }

    private void StopWatchingHostVisibility()
    {
        _hostVisibilityRepairs = 0;
        _hostVisibilityTimer?.Stop();
    }

    private void OnHostVisibilityTimerTick(DispatcherQueueTimer sender, object args)
    {
        if (_disposed || _xamlSource is null || _viewportRoot.XamlRoot is not { } root || root.IsHostVisible)
        {
            StopWatchingHostVisibility();
            return;
        }

        IntPtr bridge = Win32Interop.GetWindowFromWindowId(_xamlSource.SiteBridge.WindowId);
        bool windowIsVisible = bridge != IntPtr.Zero && NativeMethods.IsWindowVisible(bridge);
        bool topLevelWindowIsMinimized = windowIsVisible && NativeMethods.IsIconic(NativeMethods.GetAncestor(bridge, NativeMethods.GA_ROOT));

        switch (HostVisibilityPolicy.Decide(root.IsHostVisible, windowIsVisible, topLevelWindowIsMinimized, _hostVisibilityRepairs, _MaximumHostVisibilityRepairs))
        {
            case HostVisibilityAction.ShowAgain:
                _hostVisibilityRepairs++;
                WinUIDesignerLogger.LogInformation("Surface", $"The window of the island is visible and the framework says that its host is hidden, so nothing is drawn: the window is hidden and shown again (attempt {_hostVisibilityRepairs} of {_MaximumHostVisibilityRepairs}).");
                _ = NativeMethods.ShowWindow(bridge, NativeMethods.SW_HIDE);
                _ = NativeMethods.ShowWindow(bridge, NativeMethods.SW_SHOW);
                break;

            case HostVisibilityAction.GiveUp:
                sender.Stop();
                WinUIDesignerLogger.LogWarning("Surface", $"The framework still says that the host of the island is hidden while its window is visible, after the window was hidden and shown again {_hostVisibilityRepairs} times; it is left as it is until Visual Studio shows or moves the surface.");
                break;
        }
    }
}
