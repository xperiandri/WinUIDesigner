// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Diagnostics;

namespace WinUIDesigner.Surface.Markup;

/// <summary>
/// Limits the time that the retries of one request may take in all.
/// </summary>
/// <remarks>
/// Each read of a long text takes a fraction of a second, and an operation on the UI thread may take only so long
/// before its caller gives up (thirty seconds). A count of retries does not bound that for work that cannot be split
/// into operations, such as the construction actions of a document, which load every dictionary of an application in
/// one. The clock starts with the first retry that is made, not with the first read that fails: a rejection that is
/// not retried costs no time of the budget, and what passes before the first retry is not the time of a retry.
/// </remarks>
/// <param name="limit">The time, counted from the first retry, after which no retry is allowed.</param>
/// <param name="clock">Tells the time that has passed since some fixed point; the system's by default.</param>
internal sealed class RetryBudget(TimeSpan limit, Func<TimeSpan>? clock = null)
{
    private readonly Func<TimeSpan> _clock = clock ?? (() => TimeSpan.FromSeconds(Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency));

    private TimeSpan? _started;

    /// <summary>
    /// Gets whether the time of the budget has passed. A budget whose clock has not started is not exhausted.
    /// </summary>
    public bool IsExhausted => _started is { } started && _clock() - started > limit;

    /// <summary>
    /// Records that a retry is made. The first one starts the clock; later ones change nothing.
    /// </summary>
    public void Start() => _started ??= _clock();

    /// <summary>
    /// Starts afresh, for the next request.
    /// </summary>
    public void Reset() => _started = null;
}
