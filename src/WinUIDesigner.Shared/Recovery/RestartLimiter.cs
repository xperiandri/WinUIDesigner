// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;

namespace WinUIDesigner.Recovery;

/// <summary>
/// Allows a limited number of restarts in a sliding window of time. A surface that stops drawing again and again, for a
/// reason that a restart does not remove, must not be restarted without end.
/// </summary>
internal sealed class RestartLimiter
{
    private readonly object _gate = new();
    private readonly Queue<DateTime> _restarts = new();
    private readonly int _maximum;
    private readonly TimeSpan _window;

    public RestartLimiter(int maximum, TimeSpan window)
    {
        if (maximum < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maximum));
        }

        if (window <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(window));
        }

        _maximum = maximum;
        _window = window;
    }

    /// <summary>The number of restarts that <see cref="TryAcquire"/> has allowed in the window that ends at <paramref name="now"/>.</summary>
    public int Count(DateTime now)
    {
        lock (_gate)
        {
            Forget(now);
            return _restarts.Count;
        }
    }

    /// <summary>Allows a restart at <paramref name="now"/> and counts it, or says that the window is used up.</summary>
    public bool TryAcquire(DateTime now)
    {
        lock (_gate)
        {
            Forget(now);

            if (_restarts.Count >= _maximum)
            {
                return false;
            }

            _restarts.Enqueue(now);
            return true;
        }
    }

    private void Forget(DateTime now)
    {
        while (_restarts.Count > 0 && now - _restarts.Peek() >= _window)
        {
            _ = _restarts.Dequeue();
        }
    }
}
