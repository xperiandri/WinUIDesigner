// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System.Runtime.CompilerServices;
using WinUIDesigner.Surface.Common;

[TestClass]
public sealed class HostVisibilityWatchTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(250);

    [TestMethod]
    public void TheWindowIsLookedAtAtEveryIntervalWhileTheWatchRuns()
    {
        var timer = new DispatcherQueueTimer();
        var looks = new List<HostVisibilityWatch>();
        using var watch = new HostVisibilityWatch(timer, Interval, looks.Add);

        timer.Elapse();
        watch.Start();
        watch.Start();
        timer.Elapse();
        watch.Stop();
        timer.Elapse();

        Assert.AreEqual(Interval, timer.Interval);
        Assert.IsTrue(timer.IsRepeating);
        Assert.HasCount(1, looks);
        Assert.AreSame(watch, looks[0]);
    }

    [TestMethod]
    public void DisposeTakesTheHandlerAwayFromTheTimer()
    {
        var timer = new DispatcherQueueTimer();
        var watch = new HostVisibilityWatch(timer, Interval, _ => { });
        watch.Start();

        watch.Dispose();
        watch.Start();

        Assert.IsFalse(timer.IsRunning);
        Assert.IsFalse(timer.HasTickHandlers);
    }

    [TestMethod]
    public void ADisposedWatchDoesNotKeepTheSurfaceAlive()
    {
        // The timer lives on, as the native one does when the surface is closed.
        var timer = new DispatcherQueueTimer();
        WeakReference surface = WatchAndDispose(timer);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.IsFalse(surface.IsAlive);
        GC.KeepAlive(timer);
    }

    // Not inlined, so that nothing on the stack of the test refers to the surface.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference WatchAndDispose(DispatcherQueueTimer timer)
    {
        var surface = new object();
        var watch = new HostVisibilityWatch(timer, Interval, _ => GC.KeepAlive(surface));
        watch.Start();
        watch.Dispose();
        return new WeakReference(surface);
    }
}
