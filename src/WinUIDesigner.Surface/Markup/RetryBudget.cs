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
/// one.
/// </remarks>
/// <param name="limit">The time, counted from the first retry, after which no retry is allowed.</param>
/// <param name="clock">Tells the time that has passed since some fixed point; the system's by default.</param>
internal sealed class RetryBudget(TimeSpan limit, Func<TimeSpan>? clock = null)
{
    private readonly Func<TimeSpan> _clock = clock ?? (() => TimeSpan.FromSeconds(Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency));

    private TimeSpan? _started;

    /// <summary>
    /// Asks for one retry. The first one is always allowed and starts the clock.
    /// </summary>
    /// <returns><see langword="false"/> when the time of the budget has passed.</returns>
    public bool TrySpend()
    {
        TimeSpan now = _clock();
        _started ??= now;
        return now - _started.Value <= limit;
    }

    /// <summary>
    /// Starts afresh, for the next request.
    /// </summary>
    public void Reset()
    {
        _started = null;
    }
}
