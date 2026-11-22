// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace WinUIDesigner.Surface.Common;

/// <summary>
/// Provides awaitable operations for work dispatched to the WinUI UI thread.
/// </summary>
/// <remarks>
/// Visual Studio waits for a document as long as it takes, so the steps that build, change or close a document
/// (<see cref="RunAsync{T}(DispatcherQueue, string, Func{T}, CancellationToken)"/>) wait for the UI thread however long
/// it is busy: only the protocol's token and the shutdown of the dispatcher queue end them. Visual Studio blocks on its
/// synchronous requests, so only those give up after <see cref="InteractiveTimeout"/>
/// (<see cref="InvokeAsync{T}(DispatcherQueue, Func{T}, CancellationToken, string)"/> and
/// <see cref="TryInvoke{T}(DispatcherQueue, Func{T}, CancellationToken, out T, string)"/>). A request that gives up fails
/// with a <see cref="DispatcherTimeoutException"/> or a <see cref="DispatcherCanceledException"/>, which only this class
/// throws.
/// </remarks>
internal static partial class DispatcherOperation
{
    /// <summary>
    /// How long a synchronous request of Visual Studio waits for the UI thread before it gives up.
    /// </summary>
    public static readonly TimeSpan InteractiveTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Runs <paramref name="callback"/> on the UI thread for a request that Visual Studio waits for, and gives up after
    /// <see cref="InteractiveTimeout"/> with a <see cref="DispatcherTimeoutException"/>; a callback that has not started by
    /// then does not run.
    /// </summary>
    /// <param name="request">The name of the request in the log: the calling member unless it is given.</param>
    public static Task<T> InvokeAsync<T>(DispatcherQueue queue, Func<T> callback, CancellationToken cancellationToken, [CallerMemberName] string request = "")
    {
        return InvokeAsync(queue, request, callback, InteractiveTimeout, TimeProvider.System, cancellationToken);
    }

    /// <summary>
    /// Runs one step of the work on a document on the UI thread, however long the thread is busy with the steps before
    /// it: only <paramref name="cancellationToken"/> and the shutdown of the dispatcher queue end the wait, with a
    /// <see cref="DispatcherCanceledException"/>.
    /// </summary>
    /// <remarks>The step is queued before this method returns, so steps run in the order in which they were started.</remarks>
    /// <param name="step">The name of the step in the log.</param>
    public static Task<T> RunAsync<T>(DispatcherQueue queue, string step, Func<T> callback, CancellationToken cancellationToken)
    {
        return RunAsync(queue, step, callback, TimeProvider.System, cancellationToken);
    }

    /// <summary>
    /// Runs <paramref name="callback"/> on the UI thread for a synchronous observer and waits at most
    /// <see cref="InteractiveTimeout"/> for it.
    /// </summary>
    /// <param name="request">The name of the request in the log: the calling member unless it is given.</param>
    /// <returns>
    /// <see langword="true"/> with the result of the callback, or <see langword="false"/> when the request gave up (see
    /// <see cref="IsUnanswered(Exception)"/>): the observer then answers without the UI thread instead of failing.
    /// </returns>
    /// <remarks>Any other exception of the callback is thrown to the observer.</remarks>
    public static bool TryInvoke<T>(DispatcherQueue queue, Func<T> callback, CancellationToken cancellationToken, [MaybeNullWhen(false)] out T result, [CallerMemberName] string request = "")
    {
        return TryInvoke(queue, request, callback, InteractiveTimeout, TimeProvider.System, cancellationToken, out result);
    }

    /// <inheritdoc cref="TryInvoke{T}(DispatcherQueue, Func{T}, CancellationToken, out T, string)"/>
    /// <param name="timeout">How long the request waits for the UI thread.</param>
    public static bool TryInvoke<T>(DispatcherQueue queue, Func<T> callback, TimeSpan timeout, CancellationToken cancellationToken, [MaybeNullWhen(false)] out T result, [CallerMemberName] string request = "")
    {
        return TryInvoke(queue, request, callback, timeout, TimeProvider.System, cancellationToken, out result);
    }

    /// <summary>
    /// Waits for <paramref name="task"/> as long as it takes, and reports the wait with its duration when it was longer
    /// than <see cref="InteractiveTimeout"/>, the limit that the waits of the document construction used to have.
    /// </summary>
    /// <param name="what">What is waited for, in the log.</param>
    public static Task<T> WaitWithoutLimitAsync<T>(string what, Task<T> task) => WaitWithoutLimitAsync(what, task, TimeProvider.System);

    /// <summary>
    /// Whether <paramref name="exception"/> tells that a request gave up waiting for the UI thread, rather than that its
    /// callback failed: the time limit passed (<see cref="DispatcherTimeoutException"/>), or the protocol or the dispatcher
    /// queue shut down, or the queue refused the request (<see cref="DispatcherCanceledException"/>).
    /// </summary>
    /// <remarks>
    /// Only this class throws these two types, so an exception of the callback is a failure whatever its type, also a
    /// <see cref="TimeoutException"/> or an <see cref="OperationCanceledException"/> of the project's code.
    /// </remarks>
    public static bool IsUnanswered(Exception exception) => exception is DispatcherTimeoutException or DispatcherCanceledException;

    internal static Task<T> RunAsync<T>(DispatcherQueue queue, string step, Func<T> callback, TimeProvider time, CancellationToken cancellationToken)
    {
        return InvokeAsync(queue, step, callback, Timeout.InfiniteTimeSpan, time, cancellationToken);
    }

    internal static bool TryInvoke<T>(DispatcherQueue queue, string request, Func<T> callback, TimeSpan timeout, TimeProvider time, CancellationToken cancellationToken, [MaybeNullWhen(false)] out T result)
    {
        try
        {
            result = InvokeAsync(queue, request, callback, timeout, time, cancellationToken).GetAwaiter().GetResult();
            return true;
        }
        catch (Exception exception) when (IsUnanswered(exception))
        {
            // The request gave up. Nothing of that may leave a synchronous observer: ProtocolHandler.HandleMessage
            // catches only TaskCanceledException, and anything else ends the protocol's thread and with it the process.
            result = default;
            return false;
        }
    }

    internal static async Task<T> WaitWithoutLimitAsync<T>(string what, Task<T> task, TimeProvider time)
    {
        long started = time.GetTimestamp();
        T result = await task.ConfigureAwait(false);

        TimeSpan waited = time.GetElapsedTime(started);
        if (waited > InteractiveTimeout)
        {
            ReportLongWait(what, waited);
        }

        return result;
    }

    internal static async Task<T> InvokeAsync<T>(DispatcherQueue queue, string request, Func<T> callback, TimeSpan timeout, TimeProvider time, CancellationToken cancellationToken)
    {
        // The protocol has ended: nothing is queued.
        if (cancellationToken.IsCancellationRequested)
        {
            var ended = new DispatcherCanceledException("The protocol has ended.");
            ReportGaveUp(request, TimeSpan.Zero, ended);

            throw ended;
        }

        // Already on the UI thread, inside another step: run right away.
        if (queue.HasThreadAccess)
        {
            return callback();
        }

        using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        void OnShutdown(DispatcherQueue sender, DispatcherQueueShutdownStartingEventArgs args)
        {
            shutdown.Cancel();
        }

        queue.ShutdownStarting += OnShutdown;

        try
        {
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            long enqueued = time.GetTimestamp();
            long gaveUp = 0;

            if (!queue.TryEnqueue(() =>
            {
                // The request gave up before the UI thread came to it: nothing runs.
                if (completion.Task.IsCompleted || shutdown.IsCancellationRequested)
                {
                    return;
                }

                long started = time.GetTimestamp();
                Exception? failure = null;
                bool delivered;
                try
                {
                    delivered = completion.TrySetResult(callback());
                }
                catch (Exception ex)
                {
                    failure = ex;
                    delivered = completion.TrySetException(ex);
                }

                long finished = time.GetTimestamp();

                // The steps of the work on a document are traced. The requests of Visual Studio that wait for their answer
                // (hit tests, properties, snap lines) are not: they follow the user's pointing, and a trace is a write to the
                // log file, on the UI thread.
                if (timeout == Timeout.InfiniteTimeSpan)
                {
                    ReportFinishedStep(request, time.GetElapsedTime(enqueued, started), time.GetElapsedTime(started, finished));
                }

                // The request gave up while the callback ran: what the callback changed stays, but its result or
                // exception reaches nobody.
                if (!delivered)
                {
                    ReportLateFinish(request, time.GetElapsedTime(Volatile.Read(ref gaveUp), finished), failure);
                }
            }))
            {
                // The queue refuses work: it is shutting down.
                var refused = new DispatcherCanceledException("Unable to enqueue the designer request.");
                ReportGaveUp(request, TimeSpan.Zero, refused);

                throw refused;
            }

            try
            {
                return await completion.Task.WaitAsync(timeout, time, shutdown.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Volatile.Write(ref gaveUp, time.GetTimestamp());

                // The callback failed, or finished just as the wait ended: its own exception or result counts.
                if (!completion.TrySetException(ex))
                {
                    return await completion.Task.ConfigureAwait(false);
                }

                // The wait ended before the callback did: the time limit passed, or the protocol or the queue shut down.
                _ = completion.Task.Exception;
                Exception unanswered = ex is TimeoutException
                    ? new DispatcherTimeoutException($"The UI thread did not answer '{request}' within {timeout.TotalSeconds:0.#} s.", ex)
                    : new DispatcherCanceledException("The protocol or the dispatcher queue is shutting down.", ex);
                ReportGaveUp(request, time.GetElapsedTime(enqueued), unanswered);

                throw unanswered;
            }
        }
        finally
        {
            queue.ShutdownStarting -= OnShutdown;
        }
    }

    private static void ReportFinishedStep(string step, TimeSpan queueWait, TimeSpan runTime)
    {
        // Longer than a synchronous request would have waited: before the steps had no time limit, this one would have
        // timed out.
        if (queueWait + runTime > InteractiveTimeout)
        {
            ReportSlowStep(step, queueWait, runTime);
        }
        else
        {
            // Within that limit: only traced.
            ReportStep(step, queueWait, runTime);
        }
    }

    // The surface writes these reports to its log (DispatcherOperation.Log.cs); the test project records them instead.

    /// <summary>
    /// A step of the work on a document ran on the UI thread after <paramref name="queueWait"/> in the dispatcher queue,
    /// for <paramref name="runTime"/>.
    /// </summary>
    static partial void ReportStep(string step, TimeSpan queueWait, TimeSpan runTime);

    /// <summary>
    /// A step of the work on a document took longer than <see cref="InteractiveTimeout"/> altogether: it waited
    /// <paramref name="queueWait"/> in the dispatcher queue and ran for <paramref name="runTime"/>.
    /// </summary>
    static partial void ReportSlowStep(string step, TimeSpan queueWait, TimeSpan runTime);

    /// <summary>
    /// A request stopped waiting for the UI thread after <paramref name="waited"/> because of <paramref name="reason"/>.
    /// </summary>
    static partial void ReportGaveUp(string request, TimeSpan waited, Exception reason);

    /// <summary>
    /// The callback of a request that gave up finished <paramref name="late"/> after that; its result or
    /// <paramref name="exception"/> was discarded.
    /// </summary>
    static partial void ReportLateFinish(string request, TimeSpan late, Exception? exception);

    /// <summary>
    /// A wait without a time limit took <paramref name="waited"/>, longer than <see cref="InteractiveTimeout"/>.
    /// </summary>
    static partial void ReportLongWait(string what, TimeSpan waited);
}

/// <summary>
/// A request stopped waiting for the UI thread because its time limit passed: the UI thread did not answer in time.
/// </summary>
/// <remarks>See <see cref="DispatcherOperation.IsUnanswered(Exception)"/>.</remarks>
internal sealed class DispatcherTimeoutException(string message, Exception? innerException = null)
    : TimeoutException(message, innerException);

/// <summary>
/// A request stopped waiting for the UI thread because the protocol or the dispatcher queue shut down, or because the queue
/// refused the request.
/// </summary>
/// <remarks>See <see cref="DispatcherOperation.IsUnanswered(Exception)"/>.</remarks>
internal sealed class DispatcherCanceledException(string message, Exception? innerException = null)
    : OperationCanceledException(message, innerException);
