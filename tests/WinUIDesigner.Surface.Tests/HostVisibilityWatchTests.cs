// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System.Runtime.CompilerServices;
using WinUIDesigner.Surface.Common;

[TestClass]
public sealed class HostVisibilityWatchTests
{
    private const int Maximum = 3;

    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(250);

    [TestMethod]
    public void TheWindowIsLookedAtAtEveryIntervalWhileTheWatchRuns()
    {
        var timer = new DispatcherQueueTimer();
        var looks = new List<HostVisibilityWatch>();
        using var watch = new HostVisibilityWatch(timer, Interval, Maximum, looks.Add);

        timer.Elapse();
        watch.Start(startAgain: false);
        watch.Start(startAgain: false);
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
        var watch = new HostVisibilityWatch(timer, Interval, Maximum, _ => { });
        watch.Start(startAgain: false);

        watch.Dispose();
        watch.Start(startAgain: false);

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

    [TestMethod]
    public void AfterGivingUpTheWatchSaysSoOnlyOnce()
    {
        using HostVisibilityWatch watch = StartWatching(new DispatcherQueueTimer());

        for (int attempt = 1; attempt <= Maximum; attempt++)
        {
            Assert.AreEqual(HostVisibilityAction.ShowAgain, LookWhileTheWindowIsVisible(watch), $"attempt {attempt}");
            Assert.AreEqual(attempt, watch.Attempts);
        }

        Assert.AreEqual(HostVisibilityAction.GiveUp, LookWhileTheWindowIsVisible(watch));

        // The warning of the surface reports the attempts, so they are still counted when it is logged.
        Assert.AreEqual(Maximum, watch.Attempts);
        Assert.AreEqual(HostVisibilityAction.Nothing, LookWhileTheWindowIsVisible(watch));
    }

    [TestMethod]
    public void AHideAndAShowByVisualStudioAfterGivingUpStartTheAttemptsAgain()
    {
        using HostVisibilityWatch watch = StartWatching(new DispatcherQueueTimer());
        UseUpTheAttempts(watch);

        // Another tab and back at the same bounds, which brings no SetSurfacePosition.
        Assert.AreEqual(HostVisibilityAction.Nothing, LookWhileVisualStudioHidesTheWindow(watch));

        Assert.AreEqual(HostVisibilityAction.ShowAgain, LookWhileTheWindowIsVisible(watch));
        Assert.AreEqual(1, watch.Attempts);
    }

    [TestMethod]
    public void AttemptsDoNotAddUpAcrossAHideByVisualStudio()
    {
        using HostVisibilityWatch watch = StartWatching(new DispatcherQueueTimer());
        _ = LookWhileTheWindowIsVisible(watch);
        _ = LookWhileTheWindowIsVisible(watch);

        _ = LookWhileVisualStudioHidesTheWindow(watch);

        for (int attempt = 1; attempt <= Maximum; attempt++)
        {
            Assert.AreEqual(HostVisibilityAction.ShowAgain, LookWhileTheWindowIsVisible(watch), $"attempt {attempt}");
        }
    }

    [TestMethod]
    public void StartingAgainForgetsTheAttemptsAndAPlainStartDoesNot()
    {
        using HostVisibilityWatch watch = StartWatching(new DispatcherQueueTimer());
        UseUpTheAttempts(watch);

        // XamlRoot.Changed while the framework still says that the host is hidden.
        watch.Start(startAgain: false);
        Assert.AreEqual(HostVisibilityAction.Nothing, LookWhileTheWindowIsVisible(watch));

        // SetSurfacePosition.
        watch.Start(startAgain: true);
        Assert.AreEqual(HostVisibilityAction.ShowAgain, LookWhileTheWindowIsVisible(watch));
    }

    [TestMethod]
    public void StoppingForgetsTheAttempts()
    {
        var timer = new DispatcherQueueTimer();
        using HostVisibilityWatch watch = StartWatching(timer);
        UseUpTheAttempts(watch);

        watch.Stop();

        Assert.IsFalse(timer.IsRunning);
        Assert.AreEqual(0, watch.Attempts);
        watch.Start(startAgain: false);
        Assert.AreEqual(HostVisibilityAction.ShowAgain, LookWhileTheWindowIsVisible(watch));
    }

    private static HostVisibilityWatch StartWatching(DispatcherQueueTimer timer)
    {
        var watch = new HostVisibilityWatch(timer, Interval, Maximum, _ => { });
        watch.Start(startAgain: false);
        return watch;
    }

    private static void UseUpTheAttempts(HostVisibilityWatch watch)
    {
        for (int attempt = 1; attempt <= Maximum; attempt++)
        {
            _ = LookWhileTheWindowIsVisible(watch);
        }

        Assert.AreEqual(HostVisibilityAction.GiveUp, LookWhileTheWindowIsVisible(watch));
    }

    private static HostVisibilityAction LookWhileTheWindowIsVisible(HostVisibilityWatch watch)
    {
        return watch.Decide(frameworkSaysHostIsVisible: false, windowIsVisible: true, topLevelWindowIsMinimized: false);
    }

    private static HostVisibilityAction LookWhileVisualStudioHidesTheWindow(HostVisibilityWatch watch)
    {
        return watch.Decide(frameworkSaysHostIsVisible: false, windowIsVisible: false, topLevelWindowIsMinimized: false);
    }

    // Not inlined, so that nothing on the stack of the test refers to the surface.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference WatchAndDispose(DispatcherQueueTimer timer)
    {
        var surface = new object();
        var watch = new HostVisibilityWatch(timer, Interval, Maximum, _ => GC.KeepAlive(surface));
        watch.Start(startAgain: false);
        watch.Dispose();
        return new WeakReference(surface);
    }
}
