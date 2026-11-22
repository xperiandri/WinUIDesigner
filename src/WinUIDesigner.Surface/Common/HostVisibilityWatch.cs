// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;

namespace WinUIDesigner.Surface.Common;

/// <summary>
/// The timer of <c>DesignerSurface.WatchHostVisibility</c>, which looks at the window of an island while the framework says
/// that the host of the island is hidden. The timer is native and keeps its handler, and through it the surface with its
/// whole document, for as long as the timer exists; a surface that refers back to the timer makes a cycle that the garbage
/// collector cannot see. <see cref="Dispose"/> takes the handler away from the timer and lets go of it.
/// </summary>
internal sealed class HostVisibilityWatch : IDisposable
{
    private readonly Action<HostVisibilityWatch> _look;

    // A timer that nothing refers to is collected, and does not fire.
    private DispatcherQueueTimer? _timer;

    /// <param name="timer">A new timer of the thread of the island.</param>
    /// <param name="interval">How often the window is looked at.</param>
    /// <param name="look">Looks at the window, once per <paramref name="interval"/> while the watch runs.</param>
    public HostVisibilityWatch(DispatcherQueueTimer timer, TimeSpan interval, Action<HostVisibilityWatch> look)
    {
        _look = look;
        timer.Interval = interval;
        timer.IsRepeating = true;
        timer.Tick += OnTick;
        _timer = timer;
    }

    /// <summary>Looks at the window at every interval from now on, unless it already does; does nothing after <see cref="Dispose"/>.</summary>
    public void Start()
    {
        if (_timer is { IsRunning: false } timer)
        {
            timer.Start();
        }
    }

    /// <summary>Stops looking at the window until the next <see cref="Start"/>.</summary>
    public void Stop() => _timer?.Stop();

    /// <summary>Stops the timer, takes the handler away from it and lets go of it, so that the timer no longer keeps the surface alive.</summary>
    public void Dispose()
    {
        if (_timer is not { } timer)
        {
            return;
        }

        _timer = null;
        timer.Stop();
        timer.Tick -= OnTick;
    }

    private void OnTick(DispatcherQueueTimer sender, object args) => _look(this);
}
