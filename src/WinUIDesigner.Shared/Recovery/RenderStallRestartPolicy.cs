// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Concurrent;

namespace WinUIDesigner.Recovery;

/// <summary>What the extension does about a surface process whose framework has stopped drawing.</summary>
internal enum RenderStallRestart
{
    /// <summary>The process is restarted.</summary>
    Restart,

    /// <summary>
    /// The process is not restarted: it stopped at its first frame in the context of the surface that the latest restart
    /// was for, so a restart does not remove the cause.
    /// </summary>
    StopsAtFirstFrame,

    /// <summary>The process is not restarted: it has been restarted too often in a short time.</summary>
    LimitReached,
}

/// <summary>
/// Decides whether a surface process whose framework has stopped drawing is restarted. There is one policy for each
/// project: Visual Studio makes a new surface process context for a project when its designers are opened again, and a
/// count of restarts that started anew with each context would not limit anything. A process that stops at its first
/// frame is restarted once for each context of the surface (the staged files and App.xaml): a new process loads the same
/// and stops the same way, until one of them changes.
/// </summary>
internal sealed class RenderStallRestartPolicy
{
    private static readonly ConcurrentDictionary<string, RenderStallRestartPolicy> _Projects = new(StringComparer.OrdinalIgnoreCase);

    private readonly object _gate = new();
    private readonly RestartLimiter _restarts;
    private string? _restartedContext;

    public RenderStallRestartPolicy(int maximumRestarts, TimeSpan window)
    {
        _restarts = new RestartLimiter(maximumRestarts, window);
    }

    /// <summary>The policy of the project, which the first caller makes with its limits.</summary>
    public static RenderStallRestartPolicy ForProject(string? project, int maximumRestarts, TimeSpan window)
    {
        return _Projects.GetOrAdd(project ?? string.Empty, _ => new RenderStallRestartPolicy(maximumRestarts, window));
    }

    /// <summary>
    /// Whether a process that reported a stall in the context of the surface (null when the report names none) is
    /// restarted.
    /// </summary>
    public RenderStallRestart Decide(string? context, bool stoppedAtFirstFrame, DateTime now)
    {
        lock (_gate)
        {
            // The context of the latest restart stopped at its first frame again: the restart did not remove the cause.
            if (stoppedAtFirstFrame && context is not null && string.Equals(context, _restartedContext, StringComparison.Ordinal))
            {
                return RenderStallRestart.StopsAtFirstFrame;
            }

            // Restarted too often in the window.
            if (!_restarts.TryAcquire(now))
            {
                return RenderStallRestart.LimitReached;
            }

            _restartedContext = context;
            return RenderStallRestart.Restart;
        }
    }
}
