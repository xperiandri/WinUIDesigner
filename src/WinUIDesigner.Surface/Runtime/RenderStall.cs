// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Threading.Tasks;

namespace WinUIDesigner.Surface.Runtime;

/// <summary>What the watch does once the render that it asked for is over.</summary>
internal enum RenderWatchStep
{
    /// <summary>The framework still draws.</summary>
    Draws,

    /// <summary>What happened says nothing about the framework: the watch looks again later.</summary>
    LookAgain,

    /// <summary>The framework has stopped drawing.</summary>
    Stalled,
}

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
    /// What the watch does with the answer of the render that it asked for, looked at once the render is over. The render
    /// takes seconds, and one that does not complete says that the framework has stopped drawing only when nothing else
    /// explains it: the surface that it was asked of is still the one that is shown (a document that was closed, replaced
    /// or hidden meanwhile is not drawn), and no surface was frozen meanwhile (a freeze suspends rendering on purpose).
    /// </summary>
    public static RenderWatchStep AfterProbe(bool completed, bool surfaceStillShown, bool anySurfaceFrozen)
    {
        // The framework completed a render: it draws, whatever happened meanwhile.
        if (completed)
        {
            return RenderWatchStep.Draws;
        }

        // No render, but the surface was closed, replaced or hidden, or a surface was frozen: that says nothing.
        if (!surfaceStillShown || anySurfaceFrozen)
        {
            return RenderWatchStep.LookAgain;
        }

        // No render, and nothing explains it.
        return RenderWatchStep.Stalled;
    }

    /// <summary>
    /// Whether the framework stopped at the first frame of the first island of the process, by the frames that island
    /// drew (<see cref="FrameCounter.FirstIslandFrames"/>): the cause is then in what the document or App.xaml loads, which
    /// every new process loads again.
    /// </summary>
    public static bool StoppedAtFirstFrame(int? firstIslandFrames) => firstIslandFrames < FrameCounter.FramesOfAnIslandThatDraws;

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
