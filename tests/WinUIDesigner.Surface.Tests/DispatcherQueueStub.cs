global using Microsoft.UI.Dispatching;

namespace Microsoft.UI.Dispatching;

internal delegate void DispatcherQueueShutdownStartingEventHandler(DispatcherQueue sender, DispatcherQueueShutdownStartingEventArgs args);

internal sealed class DispatcherQueue(Func<Action, bool> enqueue)
{
    public bool HasThreadAccess { get; set; }

    public event DispatcherQueueShutdownStartingEventHandler? ShutdownStarting;

    public bool TryEnqueue(Action callback) => enqueue(callback);

    public void BeginShutdown() => ShutdownStarting?.Invoke(this, new DispatcherQueueShutdownStartingEventArgs());
}

internal sealed class DispatcherQueueShutdownStartingEventArgs : EventArgs
{
}

// Like the native timer, it keeps its handlers, and what they refer to, until they are taken away.
internal sealed class DispatcherQueueTimer
{
    public TimeSpan Interval { get; set; }

    public bool IsRepeating { get; set; }

    public bool IsRunning { get; private set; }

    public bool HasTickHandlers => Tick is not null;

    public event Action<DispatcherQueueTimer, object>? Tick;

    public void Start() => IsRunning = true;

    public void Stop() => IsRunning = false;

    // The interval of a running timer has passed.
    public void Elapse()
    {
        if (IsRunning)
        {
            Tick?.Invoke(this, new object());
        }
    }
}
