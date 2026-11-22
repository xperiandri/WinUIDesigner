// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Threading.Tasks;

namespace WinUIDesigner.Surface.Runtime;

/// <summary>
/// Tells whether the framework still draws after it reported an error that nothing handled. It has no WinUI types of
/// its own, so that the rules can be tested.
/// </summary>
internal static class RenderStall
{
    /// <summary>
    /// Whether the watch looks again later instead of deciding now: nothing is shown, so nothing has to be drawn, or a
    /// surface is frozen, which suspends rendering on purpose until Visual Studio unfreezes it.
    /// </summary>
    public static bool ShouldLookAgain(int shownSurfaces, bool anySurfaceFrozen) => shownSurfaces == 0 || anySurfaceFrozen;

    /// <summary>
    /// Whether a render that the framework was asked for completes: within the timeout, or within the grace that follows
    /// once the waiting thread is free again. The thread of the user interface can be busy for longer than the timeout,
    /// building a document for example, and a render waits for it; a framework that has stopped drawing never completes
    /// a render at all. Await it on the thread of the user interface.
    /// </summary>
    public static async Task<bool> CompletesAsync(Task render, TimeSpan timeout, TimeSpan grace)
    {
        if (await Task.WhenAny(render, Task.Delay(timeout)).ConfigureAwait(true) == render)
        {
            return true;
        }

        // This runs when the thread is free again, which can be long after the timeout.
        return await Task.WhenAny(render, Task.Delay(grace)).ConfigureAwait(true) == render;
    }
}
