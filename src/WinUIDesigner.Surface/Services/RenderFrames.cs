// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

namespace WinUIDesigner.Surface.Services;

/// <summary>The frames of the framework in this process, counted only while something needs them.</summary>
internal static class RenderFrames
{
    public static FrameCounter Counter { get; } = new(
        handler => CompositionTarget.Rendering += handler,
        handler => CompositionTarget.Rendering -= handler
    );
}
