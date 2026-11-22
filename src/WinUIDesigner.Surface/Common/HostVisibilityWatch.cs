// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;

namespace WinUIDesigner.Surface.Common;

/// <summary>
/// Looks at the window of an island while the framework says that the host of the island is hidden, and counts the attempts
/// to put that right (<c>DesignerSurface.WatchHostVisibility</c> looks and acts, <see cref="HostVisibilityPolicy"/> decides).
/// The timer is native and keeps its handler, and through it the surface with its whole document, for as long as the timer
/// exists; a surface that refers back to the timer makes a cycle that the garbage collector cannot see. <see cref="Dispose"/>
/// takes the handler away from the timer and lets go of it.
/// </summary>
internal sealed class HostVisibilityWatch : IDisposable
{
    private readonly int _maximumAttempts;
    private readonly Action<HostVisibilityWatch> _look;

    // A timer that nothing refers to is collected, and does not fire.
    private DispatcherQueueTimer? _timer;

    // The attempts are used up, and that was said.
    private bool _gaveUp;

    /// <param name="timer">A new timer of the thread of the island.</param>
    /// <param name="interval">How often the window is looked at.</param>
    /// <param name="maximumAttempts">How many times the window is hidden and shown again before the watch gives up.</param>
    /// <param name="look">Looks at the window, once per <paramref name="interval"/> while the watch runs.</param>
    public HostVisibilityWatch(DispatcherQueueTimer timer, TimeSpan interval, int maximumAttempts, Action<HostVisibilityWatch> look)
    {
        _maximumAttempts = maximumAttempts;
        _look = look;
        timer.Interval = interval;
        timer.IsRepeating = true;
        timer.Tick += OnTick;
        _timer = timer;
    }

    /// <summary>How many times the window was hidden and shown again since the attempts last started again.</summary>
    public int Attempts { get; private set; }

    /// <summary>Looks at the window at every interval from now on, unless it already does; does nothing after <see cref="Dispose"/>.</summary>
    /// <param name="startAgain">Earlier attempts do not count.</param>
    public void Start(bool startAgain)
    {
        // Visual Studio has placed the surface again: earlier attempts do not count.
        if (startAgain)
        {
            StartAttemptsAgain();
        }

        // The timer is not running yet (after Dispose there is none).
        if (_timer is { IsRunning: false } timer)
        {
            timer.Start();
        }
    }

    /// <summary>Stops looking at the window until the next <see cref="Start"/>; earlier attempts no longer count.</summary>
    public void Stop()
    {
        StartAttemptsAgain();
        _timer?.Stop();
    }

    /// <summary>
    /// What to do after a look at the window (<see cref="HostVisibilityPolicy.Decide"/>, with the attempts counted here).
    /// <see cref="HostVisibilityAction.GiveUp"/> comes once, and the window is still looked at afterwards, so that a look that
    /// finds the window hidden by Visual Studio, and the show after it (another tab and back), start the attempts again.
    /// </summary>
    public HostVisibilityAction Decide(bool frameworkSaysHostIsVisible, bool windowIsVisible, bool topLevelWindowIsMinimized)
    {
        // Visual Studio has hidden the window: whatever happened before, the show that follows starts the attempts again.
        if (!windowIsVisible)
        {
            StartAttemptsAgain();
        }

        switch (HostVisibilityPolicy.Decide(frameworkSaysHostIsVisible, windowIsVisible, topLevelWindowIsMinimized, Attempts, _maximumAttempts))
        {
            // One more attempt.
            case HostVisibilityAction.ShowAgain:
                Attempts++;
                return HostVisibilityAction.ShowAgain;

            // The attempts are used up: that is said once.
            case HostVisibilityAction.GiveUp when !_gaveUp:
                _gaveUp = true;
                return HostVisibilityAction.GiveUp;

            // Nothing to put right, or nothing more to say.
            default:
                return HostVisibilityAction.Nothing;
        }
    }

    /// <summary>Stops the timer, takes the handler away from it and lets go of it, so that the timer no longer keeps the surface alive.</summary>
    public void Dispose()
    {
        // Disposed already.
        if (_timer is not { } timer)
        {
            return;
        }

        _timer = null;
        timer.Stop();
        timer.Tick -= OnTick;
    }

    private void StartAttemptsAgain()
    {
        Attempts = 0;
        _gaveUp = false;
    }

    private void OnTick(DispatcherQueueTimer sender, object args) => _look(this);
}
