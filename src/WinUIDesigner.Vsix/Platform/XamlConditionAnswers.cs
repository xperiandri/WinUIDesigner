// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using WinUIDesigner.Markup;
using WinUIDesigner.Protocol;

namespace WinUIDesigner.Platform;

/// <summary>
/// Knows, on the Visual Studio side, whether the conditions of conditional XAML namespaces hold.
/// </summary>
/// <remarks>
/// <para>
/// The surface process is the authority: it runs the Windows App SDK and loads the previewed project, and it
/// rewrites prepared XAML with its own answers. This class asks it with
/// <see cref="DesignerMessageId.EvaluateXamlConditions"/> and keeps what it answers, so that documents built
/// from construction actions drop the same members.
/// </para>
/// <para>
/// Nothing here waits for the surface while a document is being built. What this process can decide itself is
/// decided from the first document on, without waiting: malformed and empty conditions, which the code both
/// processes share settles, and contracts and types and properties of the operating system, which both
/// processes ask the same Windows. The surface is still asked, to confirm, and its answer replaces the local
/// one. A condition that needs the surface, a custom condition or a type or property of the application, is
/// left as written until the surface has answered anything at all. After that, a custom condition that it has
/// not answered yet counts as not satisfied, as the surface counts a condition it cannot create, and a type
/// or property condition is still left as written, until its answer takes effect. A surface that does not
/// know the message never answers, and it loads prepared XAML with its conditional namespaces as written.
/// </para>
/// <para>
/// Answers are put into effect only in <see cref="AskAsync(IReadOnlyCollection{XamlConditionQuery}, TimeSpan, TimeSpan, TimeSpan)"/>,
/// which runs before a document is sent to the surface, and in no other place: a reply that arrives in
/// between is kept for the next call. That does not by itself keep the verdicts of a document that is
/// being built apart from a later call for another document, which may overlap its construction on another
/// thread. <see cref="XamlMemberDispositionService"/> does that: it judges a document with one set of
/// answers until the text of the document changes or the document is loaded again.
/// </para>
/// <para>
/// The class does not depend on the designer: the connection to the surface and the log are passed in as
/// delegates. Instances are safe to use from any thread.
/// </para>
/// </remarks>
internal sealed class XamlConditionAnswers
{
    // After this many requests that failed in a row, the surface is not asked again.
    private const int _MaxFailedRequests = 3;

    private readonly Func<EvaluateXamlConditionsRequest, Task<EvaluateXamlConditionsResponse>> _askSurface;
    private readonly CancellationToken _connectionClosed;
    private readonly Action _answersChanged;
    private readonly Action<string, Exception?> _report;
    private readonly Action<string>? _trace;
    private readonly XamlConditionService _conditions;
    private readonly ConcurrentDictionary<string, bool> _answeredKeys = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private readonly object _applyGate = new();

    // Guarded by _gate: the questions that wait for the request in flight to finish, the keys of the
    // questions that were sent, the request in flight together with the processing of its reply, the
    // replies that have not taken effect yet, and the long wait for the first answer of the surface.
    private readonly Dictionary<string, XamlConditionQuery> _waiting = new(StringComparer.Ordinal);
    private readonly HashSet<string> _askedKeys = new(StringComparer.Ordinal);
    private readonly List<ReceivedReply> _received = [];
    private Task? _request;
    private int _failedRequests;
    private long _firstAnswerStarted;
    private long _firstAnswerDeadline;
    private bool _firstAnswerNoticeWritten;
    private bool _firstAnswerTimeoutReported;

    private int _collecting;
    private volatile bool _surfaceAnswers;

    /// <summary>
    /// Initializes a new instance of the <see cref="XamlConditionAnswers"/> class.
    /// </summary>
    /// <param name="askSurface">Sends a request to the surface and returns its reply.</param>
    /// <param name="connectionClosed">Is canceled when the connection to the surface closes.</param>
    /// <param name="answersChanged">
    /// Called when answers took effect that change what <see cref="Evaluate"/> returns, on the thread that calls
    /// <see cref="AskAsync(IReadOnlyCollection{XamlConditionQuery}, TimeSpan, TimeSpan, TimeSpan)"/>.
    /// </param>
    /// <param name="localProbes">What this process can tell about the system; see <see cref="CreateLocalProbes"/>.</param>
    /// <param name="report">Receives a warning and the exception behind it, if any.</param>
    /// <param name="trace">Receives a message that is only of interest to somebody who looks for a problem.</param>
    public XamlConditionAnswers(
        Func<EvaluateXamlConditionsRequest, Task<EvaluateXamlConditionsResponse>> askSurface,
        CancellationToken connectionClosed,
        Action answersChanged,
        XamlConditionProbeSet localProbes,
        Action<string, Exception?> report,
        Action<string>? trace = null)
    {
        _askSurface = askSurface ?? throw new ArgumentNullException(nameof(askSurface));
        _connectionClosed = connectionClosed;
        _answersChanged = answersChanged ?? throw new ArgumentNullException(nameof(answersChanged));
        _report = report ?? throw new ArgumentNullException(nameof(report));
        _trace = trace;
        _conditions = new XamlConditionService(
            localProbes ?? throw new ArgumentNullException(nameof(localProbes)),
            message => Report($"Conditional XAML: {message}")
        );
    }

    /// <summary>
    /// Gets a value indicating whether an answer of the surface has taken effect. Conditions that only the
    /// surface can decide are evaluated only then.
    /// </summary>
    public bool SurfaceAnswers => _surfaceAnswers;

    /// <summary>
    /// Gets a value indicating whether a request is waiting for its reply. A reply that arrived is either kept
    /// for the next call of <see cref="AskAsync(IReadOnlyCollection{XamlConditionQuery}, TimeSpan, TimeSpan, TimeSpan)"/> or failed.
    /// </summary>
    internal bool IsRequestInFlight
    {
        get
        {
            lock (_gate)
            {
                return _request is not null;
            }
        }
    }

    /// <summary>
    /// Returns the probes for the conditions Visual Studio can evaluate itself, because they ask about the
    /// operating system both processes run on. The probes are missing where the Windows Runtime class behind
    /// them cannot be reached; only the surface answers then, which takes effect later.
    /// </summary>
    public static XamlConditionProbeSet CreateLocalProbes() => WindowsApiInformation.CreateProbes();

    /// <summary>
    /// Starts collecting questions: <see cref="Evaluate"/> queues what it has not been asked before but does not
    /// send it, so that everything the documents of a request ask goes in one request, by the call of
    /// <see cref="AskAsync(IReadOnlyCollection{XamlConditionQuery}, TimeSpan, TimeSpan, TimeSpan)"/> that follows.
    /// </summary>
    /// <remarks>
    /// Scanning a document evaluates the first condition it declares, and the call of <see cref="AskAsync(IReadOnlyCollection{XamlConditionQuery}, TimeSpan, TimeSpan, TimeSpan)"/>
    /// adds the others. Sent as soon as it is evaluated, the first condition would be a request of its own and the
    /// others would wait for its reply.
    /// </remarks>
    /// <returns>The scope; dispose it when the questions of the documents have been collected.</returns>
    public IDisposable BeginCollecting()
    {
        Interlocked.Increment(ref _collecting);
        return new CollectingScope(this);
    }

    /// <summary>
    /// Asks the surface the questions it has not been asked yet, waits a limited time for the answers, and puts
    /// every answer that has arrived into effect.
    /// </summary>
    /// <param name="queries">The conditions of the documents that are about to be built; may be empty.</param>
    /// <param name="timeout">How long to wait. The surface answers in the order it receives messages.</param>
    public Task AskAsync(IReadOnlyCollection<XamlConditionQuery> queries, TimeSpan timeout) => AskAsync(queries, timeout, timeout);

    /// <summary>
    /// Asks the surface the questions it has not been asked yet, waits a limited time for the answers that
    /// the documents cannot do without, and puts every answer that has arrived into effect. Called before a
    /// document is sent to the surface.
    /// </summary>
    /// <param name="queries">The conditions of the documents that are about to be built; may be empty.</param>
    /// <param name="timeout">How long to wait. The surface answers in the order it receives messages.</param>
    /// <param name="firstAnswerTimeout">
    /// How long to wait while the surface has not answered yet: a surface process that has just started
    /// answers only once it is ready. The wait is one stretch of time that starts when the first request is
    /// awaited and ends at the same moment for every document that waits for it; a surface that never answers
    /// costs it once.
    /// </param>
    /// <param name="firstAnswerNotice">
    /// After how long of the wait for the first answer a trace says that the surface is still starting;
    /// <see cref="TimeSpan.Zero"/> for no trace.
    /// </param>
    /// <remarks>
    /// Only conditions that need the surface are waited for: a custom condition, or a type or a property of
    /// the application. What this process decides itself is sent and confirmed in the background.
    /// </remarks>
    public async Task AskAsync(
        IReadOnlyCollection<XamlConditionQuery> queries,
        TimeSpan timeout,
        TimeSpan firstAnswerTimeout,
        TimeSpan firstAnswerNotice = default)
    {
        // What arrived since the last document takes effect before anything of this one is built.
        ApplyReceivedAnswers();

        bool needsSurface = false;
        foreach (XamlConditionQuery query in queries)
        {
            Enqueue(query);
            needsSurface |= NeedsSurface(query);
        }

        // Nothing here waits for the surface: the questions go out to be confirmed, and an empty request
        // still shows whether the surface answers at all. Its reply takes effect with the next document.
        if (!needsSurface)
        {
            _ = Send(force: !_surfaceAnswers);

            // A reply may have arrived while the questions were looked at.
            ApplyReceivedAnswers();
            return;
        }

        try
        {
            var elapsed = Stopwatch.StartNew();
            FirstWait? wait = null;
            while (true)
            {
                // A request of an earlier call may still be in flight; the questions of this call follow it.
                Task? request = Send(force: false);

                // Nothing was sent or is in flight: nothing was waited for, and no wait is used up.
                if (request is null)
                {
                    return;
                }

                // The long wait starts when a request is awaited, not when the call is made.
                wait ??= TakeWaitTime(timeout, firstAnswerTimeout);
                TimeSpan remaining = wait.Value.Time - elapsed.Elapsed;
                TimeSpan untilNotice = wait.Value.IsFirstAnswerWait ? GetTimeToNotice(firstAnswerNotice) : TimeSpan.Zero;
                bool noticeFirst = untilNotice > TimeSpan.Zero && untilNotice < remaining;

                // The wait goes on at the context of the caller, which is where the document is built next.
                using var timer = new CancellationTokenSource();
                bool requestFinished = remaining > TimeSpan.Zero
                    && await Task.WhenAny(request, Task.Delay(noticeFirst ? untilNotice : remaining, timer.Token)) == request;

                // The timer of a wait that the request ended would otherwise run for up to the whole wait.
                timer.Cancel();
                if (requestFinished)
                {
                    continue;
                }

                if (noticeFirst)
                {
                    WriteFirstAnswerNotice(firstAnswerNotice);
                    continue;
                }

                ReportTimeout(wait.Value);
                return;
            }
        }
        finally
        {
            ApplyReceivedAnswers();
        }
    }

    /// <summary>
    /// Evaluates a condition for the document model. The signature is that of <see cref="XamlConditionEvaluator"/>.
    /// Never blocks.
    /// </summary>
    public bool? Evaluate(string condition, Func<string, string?> resolveNamespace)
    {
        XamlConditionQuery query = XamlConditionQuery.Create(condition, resolveNamespace);

        // Malformed conditions, and custom conditions whose type cannot even be named, are decided by code
        // both processes share. Only the others are worth asking.
        XamlConditionKind kind = query.Condition.Kind;
        bool surfaceDecides = kind is XamlConditionKind.ApiContract or XamlConditionKind.Type or XamlConditionKind.Property
            || (kind == XamlConditionKind.Custom && query.TypeName is not null);

        // What this process can decide itself is decided at once: the surface runs on the same Windows and
        // reads the same code. For the rest nothing is decided without an answering surface, because it
        // loads prepared XAML as written until then, and deciding alone would make the two loading paths
        // disagree. Whatever this process answers first is compared with the answer of the surface later.
        bool surfaceAnswers = _surfaceAnswers;
        bool? holds = surfaceAnswers || _conditions.CanEvaluate(query) ? _conditions.Evaluate(query) : null;

        if (surfaceDecides && !_answeredKeys.ContainsKey(query.Key))
        {
            Enqueue(query);

            // A document that collects its questions sends them together.
            if (Volatile.Read(ref _collecting) == 0)
            {
                Send(force: false);
            }
        }

        if (!holds.HasValue && kind == XamlConditionKind.Custom && surfaceAnswers)
        {
            // Not answered yet. The surface treats a condition it cannot create as not satisfied, and so
            // does this side until it knows better.
            return false;
        }

        return holds;
    }

    private bool NeedsSurface(XamlConditionQuery query)
    {
        // Questions that the surface answered already, and those this process decides itself, need no wait.
        return !_answeredKeys.ContainsKey(query.Key)
            && query.Condition.Kind is not (XamlConditionKind.Empty or XamlConditionKind.Unknown)
            && !_conditions.CanEvaluate(query);
    }

    private void Enqueue(XamlConditionQuery query)
    {
        if (query.Condition.Kind is XamlConditionKind.Empty or XamlConditionKind.Unknown)
        {
            return;
        }

        lock (_gate)
        {
            if (!_askedKeys.Contains(query.Key))
            {
                _waiting[query.Key] = query;
            }
        }
    }

    /// <summary>
    /// Sends the waiting questions unless a request is in flight.
    /// </summary>
    /// <param name="force">Whether to send a request even when no question is waiting.</param>
    /// <returns>
    /// The request in flight, completed once its reply was processed, or <see langword="null"/> when there
    /// is none and nothing was sent.
    /// </returns>
    private Task? Send(bool force)
    {
        Exception? failure;
        lock (_gate)
        {
            if (_request is not null)
            {
                return _request;
            }

            if ((_waiting.Count == 0 && !force)
                || _failedRequests >= _MaxFailedRequests
                || _connectionClosed.IsCancellationRequested)
            {
                return null;
            }

            var queries = new List<XamlConditionQuery>(_waiting.Values);
            _waiting.Clear();
            foreach (XamlConditionQuery query in queries)
            {
                _askedKeys.Add(query.Key);
            }

            try
            {
                // The reply is completed on the thread that reads from the pipe; it is processed elsewhere.
                _request = _askSurface(XamlConditionExchange.CreateRequest(queries)).ContinueWith(
                    reply => ProcessReply(queries, reply),
                    CancellationToken.None,
                    TaskContinuationOptions.None,
                    TaskScheduler.Default
                );
                return _request;
            }
            catch (Exception ex)
            {
                Forget(queries);
                _failedRequests++;
                failure = ex;
            }
        }

        Report("Asking the surface which conditional XAML namespaces apply failed.", failure);
        return null;
    }

    private void ProcessReply(List<XamlConditionQuery> queries, Task<EvaluateXamlConditionsResponse> reply)
    {
        EvaluateXamlConditionsResponse? received = null;
        try
        {
            if (reply.IsFaulted)
            {
                Report("The surface's reply to the conditional XAML namespace request could not be read.", reply.Exception?.GetBaseException());
            }
            else if (reply.IsCanceled)
            {
                // The connection to the surface is closing; there is nothing to report.
            }
            else if (!string.IsNullOrEmpty(reply.Result?.Error))
            {
                Report($"The surface could not evaluate conditional XAML namespaces: {reply.Result!.Error}");
            }
            else if (!XamlConditionExchange.IsReplyTo(queries, reply.Result))
            {
                Report($"The surface's reply does not answer the {queries.Count} conditional XAML namespace condition(s) it was asked.");
            }
            else
            {
                received = reply.Result;
            }
        }
        catch (Exception ex)
        {
            Report("Processing the surface's answer to the conditional XAML namespace request failed.", ex);
        }

        lock (_gate)
        {
            _request = null;
            if (received is null)
            {
                // The questions may be asked again, but not without end.
                Forget(queries);
                _failedRequests++;
                return;
            }

            // A document may be being built right now; the answers wait for the next one.
            _received.Add(new ReceivedReply(queries, received));
            foreach (XamlConditionQuery query in queries)
            {
                _answeredKeys[query.Key] = true;
            }

            _failedRequests = 0;
        }

        // Questions that came up while the request was in flight; a document that collects them sends them itself.
        if (Volatile.Read(ref _collecting) == 0)
        {
            Send(force: false);
        }
    }

    /// <summary>
    /// Puts the replies that arrived into effect. Called only from <see cref="AskAsync(IReadOnlyCollection{XamlConditionQuery}, TimeSpan, TimeSpan, TimeSpan)"/>.
    /// </summary>
    private void ApplyReceivedAnswers()
    {
        // One caller at a time: a second one must not return, and go on to build, while the first has applied
        // only some of the replies it took.
        lock (_applyGate)
        {
            List<ReceivedReply> received;
            lock (_gate)
            {
                if (_received.Count == 0)
                {
                    return;
                }

                received = new List<ReceivedReply>(_received);
                _received.Clear();
            }

            bool changed = false;
            foreach (ReceivedReply reply in received)
            {
                try
                {
                    XamlConditionExchange.TryApply(reply.Queries, reply.Response, _conditions, out bool replyChanged);
                    changed |= replyChanged;
                }
                catch (Exception ex)
                {
                    Report("Processing the surface's answer to the conditional XAML namespace request failed.", ex);
                }
            }

            bool firstAnswer = !_surfaceAnswers;
            _surfaceAnswers = true;
            if (changed || firstAnswer)
            {
                try
                {
                    // Verdicts that were reached without these answers are stale.
                    _answersChanged();
                }
                catch (Exception ex)
                {
                    Report("Discarding the verdicts reached without the surface's answers failed.", ex);
                }
            }
        }
    }

    /// <summary>
    /// Returns how long a call that awaits a request may wait.
    /// </summary>
    private FirstWait TakeWaitTime(TimeSpan timeout, TimeSpan firstAnswerTimeout)
    {
        lock (_gate)
        {
            // The usual time, once the surface has answered.
            if (_surfaceAnswers || firstAnswerTimeout <= timeout)
            {
                return new FirstWait(timeout, isFirstAnswerWait: false, TimeSpan.Zero);
            }

            // The wait for the first answer is one stretch of time for all documents: the one that finds the
            // surface still starting waits for the end of it, not for a new one.
            long now = Stopwatch.GetTimestamp();
            if (_firstAnswerDeadline == 0)
            {
                _firstAnswerStarted = now;
                _firstAnswerDeadline = now + ToTicks(firstAnswerTimeout);
            }

            // The usual time, once the stretch has ended.
            TimeSpan left = FromTicks(_firstAnswerDeadline - now);
            return left > timeout
                ? new FirstWait(left, isFirstAnswerWait: true, FromTicks(now - _firstAnswerStarted))
                : new FirstWait(timeout, isFirstAnswerWait: false, TimeSpan.Zero);
        }
    }

    private TimeSpan GetTimeToNotice(TimeSpan firstAnswerNotice)
    {
        lock (_gate)
        {
            // The notice is written once, and not when the surface has answered or there is none to write.
            if (firstAnswerNotice <= TimeSpan.Zero || _firstAnswerNoticeWritten || _surfaceAnswers)
            {
                return TimeSpan.Zero;
            }

            TimeSpan sinceStart = FromTicks(Stopwatch.GetTimestamp() - _firstAnswerStarted);
            return firstAnswerNotice > sinceStart ? firstAnswerNotice - sinceStart : TimeSpan.Zero;
        }
    }

    private void WriteFirstAnswerNotice(TimeSpan firstAnswerNotice)
    {
        lock (_gate)
        {
            if (_firstAnswerNoticeWritten)
            {
                return;
            }

            _firstAnswerNoticeWritten = true;
        }

        WriteTrace($"The surface has not answered which conditional XAML namespaces apply within {firstAnswerNotice.TotalSeconds:0.#} s; it is probably still starting. The document waits for its answer.");
    }

    private void ReportTimeout(FirstWait wait)
    {
        // Every document that waits for the first answer reaches the end of it together; one warning is enough.
        if (wait.IsFirstAnswerWait)
        {
            lock (_gate)
            {
                if (_firstAnswerTimeoutReported)
                {
                    return;
                }

                _firstAnswerTimeoutReported = true;
            }
        }

        Report($"The surface did not answer which conditional XAML namespaces apply within {(wait.Time + wait.AlreadyWaited).TotalSeconds:0.#} s. Its answers take effect when the next document is loaded.");
    }

    private static long ToTicks(TimeSpan time) => (long)(time.TotalSeconds * Stopwatch.Frequency);

    private static TimeSpan FromTicks(long ticks) => TimeSpan.FromSeconds(ticks / (double)Stopwatch.Frequency);

    private void Forget(List<XamlConditionQuery> queries)
    {
        foreach (XamlConditionQuery query in queries)
        {
            _askedKeys.Remove(query.Key);
        }
    }

    private void Report(string message, Exception? exception = null)
    {
        try
        {
            _report(message, exception);
        }
        catch (Exception)
        {
            // A log that fails must not change which members a document has.
        }
    }

    private void WriteTrace(string message)
    {
        try
        {
            _trace?.Invoke(message);
        }
        catch (Exception)
        {
            // A log that fails must not change which members a document has.
        }
    }

    /// <summary>
    /// How long a call may wait for a request, and how much of the wait for the first answer has passed already.
    /// </summary>
    private readonly struct FirstWait(TimeSpan time, bool isFirstAnswerWait, TimeSpan alreadyWaited)
    {
        public TimeSpan Time { get; } = time;

        public bool IsFirstAnswerWait { get; } = isFirstAnswerWait;

        public TimeSpan AlreadyWaited { get; } = alreadyWaited;
    }

    /// <summary>
    /// A reply of the surface that has not taken effect yet, with the questions it answers.
    /// </summary>
    private sealed class ReceivedReply(List<XamlConditionQuery> queries, EvaluateXamlConditionsResponse response)
    {
        public List<XamlConditionQuery> Queries { get; } = queries;

        public EvaluateXamlConditionsResponse Response { get; } = response;
    }

    /// <summary>
    /// The scope in which <see cref="Evaluate"/> only collects questions.
    /// </summary>
    private sealed class CollectingScope(XamlConditionAnswers owner) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                Interlocked.Decrement(ref owner._collecting);
            }
        }
    }

    /// <summary>
    /// Reaches <c>Windows.Foundation.Metadata.ApiInformation</c> through reflection: the extension has no
    /// reference to Windows metadata.
    /// </summary>
    private static class WindowsApiInformation
    {
        private const string _TypeName = "Windows.Foundation.Metadata.ApiInformation, Windows, ContentType=WindowsRuntime";

        public static XamlConditionProbeSet CreateProbes()
        {
            var probes = new XamlConditionProbeSet();
            try
            {
                Type? type = Type.GetType(_TypeName, throwOnError: false);
                if (type is null)
                {
                    return probes;
                }

                MethodInfo? isContractPresent = type.GetMethod("IsApiContractPresent", [typeof(string), typeof(ushort)]);
                MethodInfo? isContractVersionPresent = type.GetMethod("IsApiContractPresent", [typeof(string), typeof(ushort), typeof(ushort)]);
                MethodInfo? isTypePresent = type.GetMethod("IsTypePresent", [typeof(string)]);
                MethodInfo? isPropertyPresent = type.GetMethod("IsPropertyPresent", [typeof(string), typeof(string)]);

                if (isContractPresent is not null && isContractVersionPresent is not null)
                {
                    probes.IsApiContractPresent = (name, major, minor) => minor.HasValue
                        ? isContractVersionPresent.Invoke(null, [name, (ushort)major, (ushort)minor.Value]) is true
                        : isContractPresent.Invoke(null, [name, (ushort)major]) is true;
                }

                if (isTypePresent is not null)
                {
                    probes.IsWindowsTypePresent = name => isTypePresent.Invoke(null, [name]) is true;
                }

                if (isPropertyPresent is not null)
                {
                    probes.IsWindowsPropertyPresent = (typeName, propertyName) => isPropertyPresent.Invoke(null, [typeName, propertyName]) is true;
                }
            }
            catch (Exception)
            {
                // The class cannot be reached from this process; the probes that were not set stay missing.
            }

            return probes;
        }
    }
}
