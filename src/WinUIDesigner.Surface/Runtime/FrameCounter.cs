// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;

namespace WinUIDesigner.Surface.Runtime;

/// <summary>
/// Counts the frames of the framework, but only while something needs them. A handler of the frame event makes the
/// framework draw at the rate of the display even when nothing changes, so it is not left attached: it counts the first
/// frames of the first island of the process, and the frames while the surface checks whether the framework still
/// draws after an error. It has no WinUI types of its own, so that the rules can be tested. Call it on the thread of the
/// user interface, which raises the frame event.
/// </summary>
internal sealed class FrameCounter
{
    /// <summary>The frames that show that the first island draws: more than its first one.</summary>
    public const int FramesOfAnIslandThatDraws = 2;

    private readonly Action<EventHandler<object>> _attach;
    private readonly Action<EventHandler<object>> _detach;
    private readonly EventHandler<object> _onFrame;
    private bool _attached;
    private bool _watching;
    private bool _measuringFirstIsland;
    private bool _firstIslandMeasured;
    private int? _firstIslandFrames;

    public FrameCounter(Action<EventHandler<object>> attach, Action<EventHandler<object>> detach)
    {
        _attach = attach;
        _detach = detach;
        _onFrame = (_, _) => OnFrame();
    }

    /// <summary>The frames counted so far, over all the times that the counter was attached.</summary>
    public int Count { get; private set; }

    /// <summary>Whether frames are counted now.</summary>
    public bool IsCounting => _attached;

    /// <summary>
    /// The frames that the first island of the process drew, counted up to <see cref="FramesOfAnIslandThatDraws"/>; null
    /// before that island was created, or when its frames could not be counted.
    /// </summary>
    public int? FirstIslandFrames => _firstIslandFrames;

    /// <summary>Counts the first frames of the first island of the process; does nothing for the islands after it.</summary>
    public void MeasureFirstIsland()
    {
        // The islands after the first one are not counted.
        if (_firstIslandMeasured)
        {
            return;
        }

        // The first island of the process, counted from its first frame if the frame event can be attached.
        _firstIslandMeasured = true;
        if (Attach())
        {
            _measuringFirstIsland = true;
            _firstIslandFrames = 0;
        }
    }

    /// <summary>Counts the frames until <see cref="StopWatching"/>; says whether they are counted.</summary>
    public bool StartWatching()
    {
        _watching = Attach();
        return _watching;
    }

    public void StopWatching()
    {
        _watching = false;
        DetachIfUnused();
    }

    /// <summary>Stops counting for good, the first island too: the framework of the process no longer draws.</summary>
    public void Stop()
    {
        _watching = false;
        _measuringFirstIsland = false;
        DetachIfUnused();
    }

    /// <summary>The frames of the first island, for the log.</summary>
    public string DescribeFirstIsland() => _firstIslandFrames switch
    {
        // No island yet, or its frames could not be counted.
        null => _firstIslandMeasured ? "not counted" : "no island yet",

        // The island has drawn more than its first frame.
        >= FramesOfAnIslandThatDraws => "more than one",

        // The island has drawn its first frame, or none yet.
        int frames => $"{frames}",
    };

    private void OnFrame()
    {
        Count = unchecked(Count + 1);

        // A frame for the watch only.
        if (!_measuringFirstIsland)
        {
            return;
        }

        // A frame of the first island, which draws once it has drawn more than its first one.
        _firstIslandFrames++;
        if (_firstIslandFrames >= FramesOfAnIslandThatDraws)
        {
            _measuringFirstIsland = false;
            DetachIfUnused();
        }
    }

    // The caller can be the handler of an unhandled exception: a frame event that cannot be attached leaves the frames
    // uncounted instead of failing it.
    private bool Attach()
    {
        if (!_attached)
        {
            try
            {
                _attach(_onFrame);
                _attached = true;
            }
            catch (Exception)
            {
                // Nothing is counted.
            }
        }

        return _attached;
    }

    private void DetachIfUnused()
    {
        if (!_attached || _watching || _measuringFirstIsland)
        {
            return;
        }

        _attached = false;
        try
        {
            _detach(_onFrame);
        }
        catch (Exception)
        {
            // The handler only counts; left attached, it costs frames that nothing reads.
        }
    }
}
