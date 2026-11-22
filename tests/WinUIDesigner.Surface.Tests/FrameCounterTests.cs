// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinUIDesigner.Surface.Runtime;

[TestClass]
public sealed class FrameCounterTests
{
    // Stands for the frame event of the framework, which raises its handlers once for every frame.
    private sealed class FrameEvent
    {
        private readonly List<EventHandler<object>> _handlers = [];

        public int Attachments { get; private set; }

        public bool IsAttached => _handlers.Count > 0;

        public void Attach(EventHandler<object> handler)
        {
            _handlers.Add(handler);
            Attachments++;
        }

        public void Detach(EventHandler<object> handler) => _handlers.Remove(handler);

        public void Raise(int frames)
        {
            for (int frame = 0; frame < frames; frame++)
            {
                foreach (EventHandler<object> handler in _handlers.ToArray())
                {
                    handler(null, EventArgs.Empty);
                }
            }
        }

        public FrameCounter CreateCounter() => new(Attach, Detach);
    }

    [TestMethod]
    public void NothingIsCountedUntilSomethingNeedsTheFrames()
    {
        var frameEvent = new FrameEvent();
        FrameCounter counter = frameEvent.CreateCounter();

        frameEvent.Raise(3);

        Assert.IsFalse(frameEvent.IsAttached);
        Assert.IsFalse(counter.IsCounting);
        Assert.AreEqual(0, counter.Count);
        Assert.IsNull(counter.FirstIslandFrames);
    }

    [TestMethod]
    public void TheFirstIslandIsCountedUntilItHasDrawnMoreThanOneFrame()
    {
        var frameEvent = new FrameEvent();
        FrameCounter counter = frameEvent.CreateCounter();

        counter.MeasureFirstIsland();
        frameEvent.Raise(1);

        Assert.IsTrue(frameEvent.IsAttached);
        Assert.AreEqual(1, counter.FirstIslandFrames);

        frameEvent.Raise(1);
        frameEvent.Raise(5);

        Assert.IsFalse(frameEvent.IsAttached);
        Assert.AreEqual(FrameCounter.FramesOfAnIslandThatDraws, counter.FirstIslandFrames);
        Assert.AreEqual(2, counter.Count);
    }

    [TestMethod]
    public void OnlyTheFirstIslandIsCounted()
    {
        var frameEvent = new FrameEvent();
        FrameCounter counter = frameEvent.CreateCounter();

        counter.MeasureFirstIsland();
        frameEvent.Raise(2);
        counter.MeasureFirstIsland();

        Assert.AreEqual(1, frameEvent.Attachments);
        Assert.IsFalse(frameEvent.IsAttached);
    }

    [TestMethod]
    public void AFirstIslandThatStopsAtItsFirstFrameKeepsItsCountWhenCountingStops()
    {
        var frameEvent = new FrameEvent();
        FrameCounter counter = frameEvent.CreateCounter();

        counter.MeasureFirstIsland();
        frameEvent.Raise(1);
        counter.Stop();

        Assert.IsFalse(frameEvent.IsAttached);
        Assert.AreEqual(1, counter.FirstIslandFrames);
    }

    [TestMethod]
    public void TheWatchCountsUntilItStops()
    {
        var frameEvent = new FrameEvent();
        FrameCounter counter = frameEvent.CreateCounter();

        Assert.IsTrue(counter.StartWatching());
        frameEvent.Raise(3);
        counter.StopWatching();
        frameEvent.Raise(3);

        Assert.AreEqual(3, counter.Count);
        Assert.IsFalse(frameEvent.IsAttached);
    }

    [TestMethod]
    public void TheWatchKeepsCountingAfterTheFirstIslandHasDrawn()
    {
        var frameEvent = new FrameEvent();
        FrameCounter counter = frameEvent.CreateCounter();

        counter.MeasureFirstIsland();
        _ = counter.StartWatching();
        frameEvent.Raise(4);

        Assert.AreEqual(1, frameEvent.Attachments);
        Assert.IsTrue(frameEvent.IsAttached);

        counter.StopWatching();

        Assert.IsFalse(frameEvent.IsAttached);
    }

    [TestMethod]
    public void TheFirstIslandKeepsBeingCountedAfterTheWatchHasStopped()
    {
        var frameEvent = new FrameEvent();
        FrameCounter counter = frameEvent.CreateCounter();

        counter.MeasureFirstIsland();
        _ = counter.StartWatching();
        counter.StopWatching();

        Assert.IsTrue(frameEvent.IsAttached);

        frameEvent.Raise(2);

        Assert.IsFalse(frameEvent.IsAttached);
    }

    [TestMethod]
    public void AFrameEventThatCannotBeAttachedLeavesTheFramesUncounted()
    {
        var counter = new FrameCounter(_ => throw new InvalidOperationException("no frame event"), _ => { });

        counter.MeasureFirstIsland();

        Assert.IsFalse(counter.StartWatching());
        Assert.IsFalse(counter.IsCounting);
        Assert.IsNull(counter.FirstIslandFrames);
        Assert.AreEqual("not counted", counter.DescribeFirstIsland());
    }

    [TestMethod]
    public void TheFirstIslandIsDescribedForTheLog()
    {
        var frameEvent = new FrameEvent();
        FrameCounter counter = frameEvent.CreateCounter();

        Assert.AreEqual("no island yet", counter.DescribeFirstIsland());

        counter.MeasureFirstIsland();
        frameEvent.Raise(1);

        Assert.AreEqual("1", counter.DescribeFirstIsland());

        frameEvent.Raise(1);

        Assert.AreEqual("more than one", counter.DescribeFirstIsland());
    }
}
