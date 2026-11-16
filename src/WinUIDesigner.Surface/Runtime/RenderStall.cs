// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

namespace WinUIDesigner.Surface.Runtime;

/// <summary>
/// Tells whether the framework still draws after it reported an error that nothing handled. It has no WinUI types of
/// its own, so that the rule can be tested.
/// </summary>
internal static class RenderStall
{
    /// <summary>
    /// The number of frames that a surface that is drawing completes within the time that is watched (a few seconds, at
    /// about sixty frames in a second). Fewer mean that it has stopped.
    /// </summary>
    public const int MinimumFrames = 5;

    /// <summary>
    /// Whether the frames that were counted at the start of the watched time and at its end are too few for a surface
    /// that draws. The counter may wrap around.
    /// </summary>
    public static bool IsStalled(int framesAtStart, int framesAtEnd)
    {
        return unchecked(framesAtEnd - framesAtStart) < MinimumFrames;
    }
}
