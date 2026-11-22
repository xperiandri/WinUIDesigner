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

    /// <summary>The process is not restarted: it has been restarted too often in a short time.</summary>
    LimitReached,
}

/// <summary>
/// Decides whether a surface process whose framework has stopped drawing is restarted. There is one policy for each
/// project: Visual Studio makes a new surface process context for a project when its designers are opened again, and a
/// count of restarts that started anew with each context would not limit anything.
/// </summary>
internal sealed class RenderStallRestartPolicy
{
    private static readonly ConcurrentDictionary<string, RenderStallRestartPolicy> _Projects = new(StringComparer.OrdinalIgnoreCase);

    private readonly RestartLimiter _restarts;

    public RenderStallRestartPolicy(int maximumRestarts, TimeSpan window)
    {
        _restarts = new RestartLimiter(maximumRestarts, window);
    }

    /// <summary>The policy of the project, which the first caller makes with its limits.</summary>
    public static RenderStallRestartPolicy ForProject(string? project, int maximumRestarts, TimeSpan window)
    {
        return _Projects.GetOrAdd(project ?? string.Empty, _ => new RenderStallRestartPolicy(maximumRestarts, window));
    }

    public RenderStallRestart Decide(DateTime now) => _restarts.TryAcquire(now) ? RenderStallRestart.Restart : RenderStallRestart.LimitReached;
}
