// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinUIDesigner.Surface.Runtime;

[TestClass]
public sealed class RenderStallTests
{
    private static readonly TimeSpan _Short = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan _Long = TimeSpan.FromSeconds(10);

    [TestMethod]
    public void TheWatchLooksAgainWhenNothingIsShown()
    {
        Assert.IsTrue(RenderStall.ShouldLookAgain(shownSurfaces: 0, anySurfaceFrozen: false));
    }

    [TestMethod]
    public void TheWatchLooksAgainWhileASurfaceIsFrozen()
    {
        // A freeze suspends rendering on purpose, so a render that does not complete says nothing about the framework.
        Assert.IsTrue(RenderStall.ShouldLookAgain(shownSurfaces: 2, anySurfaceFrozen: true));
    }

    [TestMethod]
    public void TheWatchDecidesWhenASurfaceIsShownAndNothingIsFrozen()
    {
        Assert.IsFalse(RenderStall.ShouldLookAgain(shownSurfaces: 1, anySurfaceFrozen: false));
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    public void AFirstIslandThatDrewAtMostOneFrameStoppedAtTheFirstFrame(int frames)
    {
        Assert.IsTrue(RenderStall.StoppedAtFirstFrame(frames));
    }

    [TestMethod]
    public void AFirstIslandThatDrewStoppedLater()
    {
        Assert.IsFalse(RenderStall.StoppedAtFirstFrame(FrameCounter.FramesOfAnIslandThatDraws));
    }

    [TestMethod]
    public void FramesThatWereNotCountedSayNothingAboutTheFirstFrame() => Assert.IsFalse(RenderStall.StoppedAtFirstFrame(null));

    [TestMethod]
    public async Task ARenderThatCompletesDraws() => Assert.IsTrue(await RenderStall.CompletesAsync(Task.CompletedTask, _Short, _Short));

    [TestMethod]
    public async Task ARenderThatNeverCompletesDoesNotDraw()
    {
        var render = new TaskCompletionSource();

        Assert.IsFalse(await RenderStall.CompletesAsync(render.Task, _Short, _Short));
    }

    [TestMethod]
    public async Task ARenderThatCompletesAfterTheTimeoutWithinTheGraceDraws()
    {
        // The render waited for a thread that was busy for longer than the timeout.
        var render = new TaskCompletionSource();
        _ = Task.Delay(TimeSpan.FromMilliseconds(300)).ContinueWith(_ => render.TrySetResult(), TaskScheduler.Default);

        Assert.IsTrue(await RenderStall.CompletesAsync(render.Task, TimeSpan.FromMilliseconds(20), _Long));
    }
}
