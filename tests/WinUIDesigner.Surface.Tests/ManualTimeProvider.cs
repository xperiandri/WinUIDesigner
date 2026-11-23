// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

/// <summary>
/// A clock that moves only when a test calls <see cref="Advance(TimeSpan)"/>, and then runs the timers that have come
/// due, so that a test passes a time limit without waiting for it.
/// </summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private readonly object _gate = new();
    private readonly List<ManualTimer> _timers = [];
    private readonly SemaphoreSlim _timerCreated = new(0);
    private long _ticks;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp()
    {
        lock (_gate)
        {
            return _ticks;
        }
    }

    public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddTicks(GetTimestamp());

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        _timerCreated.Release();

        return timer;
    }

    /// <summary>
    /// Moves the clock forward by <paramref name="delta"/> and runs each timer that comes due on the way, at its time.
    /// </summary>
    public void Advance(TimeSpan delta)
    {
        long target;
        lock (_gate)
        {
            target = _ticks + delta.Ticks;
        }

        while (TakeNextDue(target) is { } timer)
        {
            timer.Fire();
        }
    }

    /// <summary>
    /// Waits until the next timer is created: a request creates its timer after it has queued its callback. Waits a few
    /// seconds of real time at most, so that a broken test fails instead of hanging.
    /// </summary>
    public async Task WaitForTimerAsync()
    {
        if (!await _timerCreated.WaitAsync(TimeSpan.FromSeconds(10)))
        {
            throw new TimeoutException("No timer was created.");
        }
    }

    private ManualTimer? TakeNextDue(long target)
    {
        lock (_gate)
        {
            ManualTimer? next = null;
            foreach (ManualTimer timer in _timers)
            {
                if (timer.DueAt <= target && (next is null || timer.DueAt < next.DueAt))
                {
                    next = timer;
                }
            }

            // No timer comes due before the target: the clock arrives there.
            if (next is null)
            {
                _ticks = target;
                return null;
            }

            // The next timer: the clock stops at its time, and it is set again if it repeats.
            _ticks = Math.Max(_ticks, next.DueAt);
            if (next.Period > 0)
            {
                next.DueAt += next.Period;
            }
            else
            {
                _timers.Remove(next);
            }

            return next;
        }
    }

    private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        private bool _disposed;

        public long DueAt { get; set; }

        public long Period { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (owner._gate)
            {
                if (_disposed)
                {
                    return false;
                }

                owner._timers.Remove(this);
                if (dueTime != Timeout.InfiniteTimeSpan)
                {
                    DueAt = owner._ticks + dueTime.Ticks;
                    Period = period == Timeout.InfiniteTimeSpan ? 0 : period.Ticks;
                    owner._timers.Add(this);
                }

                return true;
            }
        }

        public void Fire() => callback(state);

        public void Dispose()
        {
            lock (owner._gate)
            {
                _disposed = true;
                owner._timers.Remove(this);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
