// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using WinUIDesigner.Surface.Common;
using Action = System.Action;

/// <summary>
/// The time limits of the work on the UI thread: the work on a document waits as long as the UI thread is busy, and only
/// the synchronous requests of Visual Studio give up, without an exception for the observer that answers them.
/// </summary>
[TestClass]
public sealed class DispatcherOperationTests
{
    // A task that has not completed after this much real time never will: its test fails instead of hanging the run.
    private static readonly TimeSpan s_patience = TimeSpan.FromSeconds(10);

    [TestMethod]
    public async Task DocumentStepWaitsForABusyUIThreadLongerThanTheInteractiveLimit()
    {
        const string Step = nameof(DocumentStepWaitsForABusyUIThreadLongerThanTheInteractiveLimit);
        var time = new ManualTimeProvider();
        Action? queued = null;
        var queue = new DispatcherQueue(action => { queued = action; return true; });

        Task<int> step = DispatcherOperation.RunAsync(queue, Step, () => 42, time, default);
        time.Advance(TimeSpan.FromMinutes(10));

        Assert.IsFalse(step.IsCompleted);
        queued!();
        Assert.AreEqual(42, await Promptly(step));
        Assert.AreEqual(TimeSpan.FromMinutes(10), Assert.ContainsSingle(DispatcherReports.Of(DispatcherReportKind.SlowStep, Step)).Duration);
        Assert.IsEmpty(DispatcherReports.Of(DispatcherReportKind.GaveUp, Step));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task DocumentStepEndsWithTheProtocolOrTheQueueAndItsCallbackNeverRuns(bool queueShutsDown)
    {
        Action? queued = null;
        bool changed = false;
        var queue = new DispatcherQueue(action => { queued = action; return true; });
        using var protocol = new CancellationTokenSource();

        Task<bool> step = DispatcherOperation.RunAsync(queue, nameof(DocumentStepEndsWithTheProtocolOrTheQueueAndItsCallbackNeverRuns), () => changed = true, new ManualTimeProvider(), protocol.Token);
        if (queueShutsDown)
        {
            // The dispatcher queue shuts down while the step waits.
            queue.BeginShutdown();
        }
        else
        {
            // The protocol ends while the step waits.
            protocol.Cancel();
        }

        DispatcherCanceledException ended = await Assert.ThrowsExactlyAsync<DispatcherCanceledException>(() => Promptly(step));
        queued!();
        Assert.IsFalse(changed);
        Assert.IsTrue(DispatcherOperation.IsUnanswered(ended));
    }

    [TestMethod]
    public async Task StepsAreQueuedInTheOrderOfTheCallsBeforeTheCallReturns()
    {
        var time = new ManualTimeProvider();
        var queued = new List<Action>();
        var order = new List<string>();
        var queue = new DispatcherQueue(action => { queued.Add(action); return true; });

        Task<int> first = DispatcherOperation.RunAsync(queue, $"{nameof(StepsAreQueuedInTheOrderOfTheCallsBeforeTheCallReturns)} 1", () => { order.Add("first"); return 1; }, time, default);
        Task<int> second = DispatcherOperation.RunAsync(queue, $"{nameof(StepsAreQueuedInTheOrderOfTheCallsBeforeTheCallReturns)} 2", () => { order.Add("second"); return 2; }, time, default);

        // Both steps are in the queue when RunAsync returns, so nothing that a handler does next can overtake them:
        // SetFreezeState relies on it for the order of a freeze and the unfreeze after it.
        Assert.HasCount(2, queued);
        foreach (Action action in queued)
        {
            action();
        }

        Assert.AreEqual(1, await Promptly(first));
        Assert.AreEqual(2, await Promptly(second));
        CollectionAssert.AreEqual(new[] { "first", "second" }, order);
    }

    [TestMethod]
    public async Task InteractiveRequestStillGivesUpAndItsQueuedCallbackNeverRuns()
    {
        const string Request = nameof(InteractiveRequestStillGivesUpAndItsQueuedCallbackNeverRuns);
        var time = new ManualTimeProvider();
        Action? queued = null;
        bool changed = false;
        var queue = new DispatcherQueue(action => { queued = action; return true; });

        Task<bool> pending = DispatcherOperation.InvokeAsync(queue, Request, () => changed = true, DispatcherOperation.InteractiveTimeout, time, default);
        time.Advance(DispatcherOperation.InteractiveTimeout - TimeSpan.FromTicks(1));
        Assert.IsFalse(pending.IsCompleted);

        time.Advance(TimeSpan.FromTicks(1));
        DispatcherTimeoutException gaveUp = await Assert.ThrowsExactlyAsync<DispatcherTimeoutException>(() => Promptly(pending));
        queued!();

        Assert.IsFalse(changed);
        Assert.IsTrue(DispatcherOperation.IsUnanswered(gaveUp));
        Assert.IsInstanceOfType<TimeoutException>(Assert.ContainsSingle(DispatcherReports.Of(DispatcherReportKind.GaveUp, Request)).Exception);
        Assert.IsEmpty(DispatcherReports.Of(DispatcherReportKind.Step, Request));
    }

    [TestMethod]
    public async Task CallbackThatOutlivesItsRequestIsReportedOnce()
    {
        const string Request = nameof(CallbackThatOutlivesItsRequestIsReportedOnce);
        var time = new ManualTimeProvider();
        Action? queued = null;
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var queue = new DispatcherQueue(action => { queued = action; return true; });

        Task<int> pending = DispatcherOperation.InvokeAsync(queue, Request, () =>
        {
            started.Set();
            release.Wait(TimeSpan.FromSeconds(10));
            return 1;
        }, DispatcherOperation.InteractiveTimeout, time, default);

        // The UI thread takes the callback, and the callback runs on past the time limit of its request.
        Task uiThread = Task.Run(queued!);
        try
        {
            Assert.IsTrue(started.Wait(TimeSpan.FromSeconds(10)));
            time.Advance(DispatcherOperation.InteractiveTimeout);
            await Assert.ThrowsExactlyAsync<DispatcherTimeoutException>(() => Promptly(pending));
            time.Advance(TimeSpan.FromSeconds(5));
        }
        finally
        {
            release.Set();
        }

        await uiThread;

        Assert.AreEqual(TimeSpan.FromSeconds(5), Assert.ContainsSingle(DispatcherReports.Of(DispatcherReportKind.LateFinish, Request)).Duration);
        Assert.HasCount(1, DispatcherReports.Of(DispatcherReportKind.GaveUp, Request));
    }

    [TestMethod]
    [DataRow(30, "StalledGetProperties")]
    [DataRow(2, "StalledSetFreezeState")]
    public async Task SynchronousRequestOfAStalledUIThreadIsAnsweredWithoutAnException(int seconds, string request)
    {
        TimeSpan timeout = TimeSpan.FromSeconds(seconds);
        var time = new ManualTimeProvider();
        Action? queued = null;
        bool changed = false;
        var queue = new DispatcherQueue(action => { queued = action; return true; });

        Task<bool> answered = Task.Run(() => DispatcherOperation.TryInvoke(queue, request, () => changed = true, timeout, time, default, out _));
        await time.WaitForTimerAsync();
        time.Advance(timeout);

        Assert.IsFalse(await Promptly(answered));
        queued!();
        Assert.IsFalse(changed);
        Assert.IsInstanceOfType<DispatcherTimeoutException>(Assert.ContainsSingle(DispatcherReports.Of(DispatcherReportKind.GaveUp, request)).Exception);
    }

    [TestMethod]
    public async Task SynchronousRequestIsAnsweredWithoutAnExceptionWhenTheQueueShutsDown()
    {
        var time = new ManualTimeProvider();
        Action? queued = null;
        bool changed = false;
        var queue = new DispatcherQueue(action => { queued = action; return true; });

        Task<bool> answered = Task.Run(() => DispatcherOperation.TryInvoke(queue, nameof(SynchronousRequestIsAnsweredWithoutAnExceptionWhenTheQueueShutsDown), () => changed = true, DispatcherOperation.InteractiveTimeout, time, default, out _));
        await time.WaitForTimerAsync();
        queue.BeginShutdown();

        Assert.IsFalse(await Promptly(answered));
        queued!();
        Assert.IsFalse(changed);
    }

    [TestMethod]
    public void SynchronousRequestThatTheQueueRefusesIsAnsweredWithoutAnException()
    {
        var queue = new DispatcherQueue(_ => false);

        Assert.IsFalse(DispatcherOperation.TryInvoke(queue, () => 1, default, out _));
        Assert.IsInstanceOfType<DispatcherCanceledException>(Assert.ContainsSingle(DispatcherReports.Of(DispatcherReportKind.GaveUp, nameof(SynchronousRequestThatTheQueueRefusesIsAnsweredWithoutAnException))).Exception);
    }

    [TestMethod]
    public void SynchronousRequestOfACancelledProtocolIsAnsweredWithoutAnException()
    {
        bool ran = false;
        var queue = new DispatcherQueue(action => { action(); return true; });
        using var protocol = new CancellationTokenSource();
        protocol.Cancel();

        Assert.IsFalse(DispatcherOperation.TryInvoke(queue, () => ran = true, protocol.Token, out _));
        Assert.IsFalse(ran);
        Assert.IsInstanceOfType<DispatcherCanceledException>(Assert.ContainsSingle(DispatcherReports.Of(DispatcherReportKind.GaveUp, nameof(SynchronousRequestOfACancelledProtocolIsAnsweredWithoutAnException))).Exception);
    }

    [TestMethod]
    public void FailureOfTheCallbackOfASynchronousRequestReachesTheObserver()
    {
        var queue = new DispatcherQueue(action => { action(); return true; });

        Assert.ThrowsExactly<InvalidOperationException>(() => DispatcherOperation.TryInvoke<int>(queue, () => throw new InvalidOperationException(), default, out _));
    }

    [TestMethod]
    [DataRow(typeof(TimeoutException))]
    [DataRow(typeof(OperationCanceledException))]
    [DataRow(typeof(TaskCanceledException))]
    public async Task CallbackThatThrowsWhatGivingUpThrowsHasFailedAndDidNotGiveUp(Type exceptionType)
    {
        string request = $"{nameof(CallbackThatThrowsWhatGivingUpThrowsHasFailedAndDidNotGiveUp)} {exceptionType.Name}";
        var failure = (Exception)Activator.CreateInstance(exceptionType)!;
        var time = new ManualTimeProvider();
        var queue = new DispatcherQueue(action => { action(); return true; });

        // A synchronous observer gets the exception of the callback, like any other: it is no answer without the UI thread.
        Exception observed = Assert.Throws<Exception>(() => DispatcherOperation.TryInvoke<int>(queue, request, () => throw failure, DispatcherOperation.InteractiveTimeout, time, default, out _));
        Assert.AreSame(failure, observed);
        Assert.IsFalse(DispatcherOperation.IsUnanswered(observed));

        // A step that fails like this has failed: the load of the prepared XAML falls back to construction actions then.
        Exception failed = await Assert.ThrowsAsync<Exception>(() => Promptly(DispatcherOperation.RunAsync<int>(queue, request, () => throw failure, time, default)));
        Assert.AreSame(failure, failed);
        Assert.IsFalse(DispatcherOperation.IsUnanswered(failed));
        Assert.IsEmpty(DispatcherReports.Of(DispatcherReportKind.GaveUp, request));
    }

    [TestMethod]
    [DataRow(typeof(TimeoutException))]
    [DataRow(typeof(OperationCanceledException))]
    [DataRow(typeof(TaskCanceledException))]
    [DataRow(typeof(InvalidOperationException))]
    [DataRow(typeof(COMException))]
    public void FailureIsToldApartFromGivingUp(Type exceptionType)
    {
        var exception = (Exception)Activator.CreateInstance(exceptionType)!;

        Assert.IsFalse(DispatcherOperation.IsUnanswered(exception));
    }

    [TestMethod]
    public void ExceptionsOfGivingUpAreUnanswered()
    {
        Assert.IsTrue(DispatcherOperation.IsUnanswered(new DispatcherTimeoutException("The time limit passed.")));
        Assert.IsTrue(DispatcherOperation.IsUnanswered(new DispatcherCanceledException("The protocol has ended.")));
    }

    [TestMethod]
    public async Task StepIsReportedWithItsQueueWaitAndItsRunTime()
    {
        const string Step = nameof(StepIsReportedWithItsQueueWaitAndItsRunTime);
        var time = new ManualTimeProvider();
        Action? queued = null;
        var queue = new DispatcherQueue(action => { queued = action; return true; });

        Task<int> pending = DispatcherOperation.RunAsync(queue, Step, () =>
        {
            time.Advance(TimeSpan.FromSeconds(7));
            return 1;
        }, time, default);
        time.Advance(TimeSpan.FromSeconds(5));
        queued!();
        await Promptly(pending);

        DispatcherReport report = Assert.ContainsSingle(DispatcherReports.Of(DispatcherReportKind.Step, Step));
        Assert.AreEqual(TimeSpan.FromSeconds(5), report.Duration);
        Assert.AreEqual(TimeSpan.FromSeconds(7), report.RunTime);
        Assert.IsEmpty(DispatcherReports.Of(DispatcherReportKind.SlowStep, Step));
    }

    [TestMethod]
    [DataRow(45, 0, true)]
    [DataRow(0, 45, true)]
    [DataRow(20, 11, true)]
    [DataRow(20, 10, false)]
    public async Task StepThatTookLongerThanTheInteractiveLimitIsReportedAsSlow(int waitSeconds, int runSeconds, bool slow)
    {
        string step = $"{nameof(StepThatTookLongerThanTheInteractiveLimitIsReportedAsSlow)} {waitSeconds}+{runSeconds}";
        var time = new ManualTimeProvider();
        Action? queued = null;
        var queue = new DispatcherQueue(action => { queued = action; return true; });

        Task<int> pending = DispatcherOperation.RunAsync(queue, step, () =>
        {
            time.Advance(TimeSpan.FromSeconds(runSeconds));
            return 1;
        }, time, default);
        time.Advance(TimeSpan.FromSeconds(waitSeconds));
        queued!();
        await Promptly(pending);

        // The wait and the run time of a step together, not each of them, are compared with the interactive limit.
        DispatcherReport report = Assert.ContainsSingle(DispatcherReports.Of(slow ? DispatcherReportKind.SlowStep : DispatcherReportKind.Step, step));
        Assert.AreEqual(TimeSpan.FromSeconds(waitSeconds), report.Duration);
        Assert.AreEqual(TimeSpan.FromSeconds(runSeconds), report.RunTime);
        Assert.IsEmpty(DispatcherReports.Of(slow ? DispatcherReportKind.Step : DispatcherReportKind.SlowStep, step));
    }

    [TestMethod]
    public async Task InteractiveRequestIsNotTracedAsAStep()
    {
        const string Request = nameof(InteractiveRequestIsNotTracedAsAStep);
        var queue = new DispatcherQueue(action => { action(); return true; });

        Assert.AreEqual(1, await Promptly(DispatcherOperation.InvokeAsync(queue, Request, () => 1, DispatcherOperation.InteractiveTimeout, new ManualTimeProvider(), default)));

        Assert.IsEmpty(DispatcherReports.Of(DispatcherReportKind.Step, Request));
        Assert.IsEmpty(DispatcherReports.Of(DispatcherReportKind.SlowStep, Request));
    }

    [TestMethod]
    public async Task WaitWithoutALimitCompletesAfterTheInteractiveLimitAndIsReportedWithItsDuration()
    {
        const string What = nameof(WaitWithoutALimitCompletesAfterTheInteractiveLimitAndIsReportedWithItsDuration);
        var time = new ManualTimeProvider();
        var gate = new TaskCompletionSource<bool>();

        Task<bool> waiting = DispatcherOperation.WaitWithoutLimitAsync(What, gate.Task, time);
        time.Advance(TimeSpan.FromSeconds(45));
        Assert.IsFalse(waiting.IsCompleted);
        gate.SetResult(true);

        Assert.IsTrue(await Promptly(waiting));
        Assert.AreEqual(TimeSpan.FromSeconds(45), Assert.ContainsSingle(DispatcherReports.Of(DispatcherReportKind.LongWait, What)).Duration);
    }

    [TestMethod]
    public async Task WaitWithinTheInteractiveLimitIsNotReported()
    {
        const string What = nameof(WaitWithinTheInteractiveLimitIsNotReported);
        var time = new ManualTimeProvider();
        var gate = new TaskCompletionSource<bool>();

        Task<bool> waiting = DispatcherOperation.WaitWithoutLimitAsync(What, gate.Task, time);
        time.Advance(DispatcherOperation.InteractiveTimeout);
        gate.SetResult(true);

        Assert.IsTrue(await Promptly(waiting));
        Assert.IsEmpty(DispatcherReports.Of(DispatcherReportKind.LongWait, What));
    }

    private static async Task<T> Promptly<T>(Task<T> task)
    {
        try
        {
            return await task.WaitAsync(s_patience);
        }
        catch (TimeoutException) when (!task.IsCompleted)
        {
            Assert.Fail($"The task did not complete within {s_patience.TotalSeconds} s of real time.");
            throw;
        }
    }
}
