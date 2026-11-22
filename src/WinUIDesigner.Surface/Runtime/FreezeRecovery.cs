// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;
using System.Linq;

namespace WinUIDesigner.Surface.Runtime;

/// <summary>What the timer of a freeze does when it fires.</summary>
internal enum FreezeTimerStep
{
    Nothing,
    Unfreeze,
    ResumeRendering,
}

/// <summary>
/// How a surface brings back rendering that a freeze suspended. Suspending rendering is a switch of the whole process,
/// Visual Studio unfreezes a surface in a frame of its own window, which a window that is not drawn does not produce,
/// and the resumption can fail. It has no WinUI types of its own, so that the rules can be tested.
/// </summary>
internal static class FreezeRecovery
{
    /// <summary>How long a surface stays frozen before it unfreezes itself.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <summary>How long a surface waits before it tries again to resume rendering that could not be resumed.</summary>
    public static readonly TimeSpan ResumeRetryInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>How many times a surface tries again to resume rendering.</summary>
    public const int MaximumResumeRetries = 20;

    public static FreezeTimerStep OnTimer(bool disposed, bool frozen, bool renderingSuspended)
    {
        // A surface that is gone does nothing.
        if (disposed)
        {
            return FreezeTimerStep.Nothing;
        }

        // Visual Studio did not unfreeze the surface in time.
        if (frozen)
        {
            return FreezeTimerStep.Unfreeze;
        }

        // The surface was unfrozen, but its rendering could not be resumed.
        return renderingSuspended ? FreezeTimerStep.ResumeRendering : FreezeTimerStep.Nothing;
    }

    /// <summary>Whether a resumption is tried again after it has failed the given number of times.</summary>
    public static bool ShouldRetryResume(int failures) => failures < MaximumResumeRetries;

    /// <summary>
    /// Whether rendering may be resumed when Visual Studio shows or moves a surface: only while no surface is frozen, as
    /// a frozen one keeps rendering suspended on purpose.
    /// </summary>
    public static bool MayResumeRendering(IEnumerable<bool> surfacesFrozen) => !surfacesFrozen.Contains(true);
}
