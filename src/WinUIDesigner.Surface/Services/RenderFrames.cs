// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System.Threading;

namespace WinUIDesigner.Surface.Services;

/// <summary>
/// Counts the frames that the framework completes. A surface that is shown draws about sixty frames in a second, so a
/// counter that stands still while a surface is shown means that the framework no longer draws.
/// </summary>
internal static class RenderFrames
{
    private static int s_count;
    private static bool s_started;

    public static int Count => Volatile.Read(ref s_count);

    /// <summary>Starts counting. Call it on the thread of the user interface; it does nothing the second time.</summary>
    public static void EnsureStarted()
    {
        if (s_started)
        {
            return;
        }

        s_started = true;
        CompositionTarget.Rendering += (_, _) => Interlocked.Increment(ref s_count);
    }
}
