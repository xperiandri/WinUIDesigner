// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System.Collections.Concurrent;

namespace WinUIDesigner.Surface.Common;

// The test build of DispatcherOperation: the reports that the surface writes to its log are recorded instead.
internal static partial class DispatcherOperation
{
    static partial void ReportStep(string step, TimeSpan queueWait, TimeSpan runTime)
    {
        DispatcherReports.Add(new(DispatcherReportKind.Step, step, queueWait, null));
    }

    static partial void ReportGaveUp(string request, TimeSpan waited, Exception reason)
    {
        DispatcherReports.Add(new(DispatcherReportKind.GaveUp, request, waited, reason));
    }

    static partial void ReportLateFinish(string request, TimeSpan late, Exception? exception)
    {
        DispatcherReports.Add(new(DispatcherReportKind.LateFinish, request, late, exception));
    }

    static partial void ReportLongWait(string what, TimeSpan waited)
    {
        DispatcherReports.Add(new(DispatcherReportKind.LongWait, what, waited, null));
    }
}

internal enum DispatcherReportKind
{
    Step,
    GaveUp,
    LateFinish,
    LongWait,
}

/// <summary>
/// A report of <see cref="DispatcherOperation"/>: for a step, <see cref="Duration"/> is its wait in the dispatcher queue.
/// </summary>
internal sealed record DispatcherReport(DispatcherReportKind Kind, string Name, TimeSpan Duration, Exception? Exception);

/// <summary>
/// The reports of <see cref="DispatcherOperation"/> in the tests. Each test gives its requests a name of its own and reads
/// only the reports of that name.
/// </summary>
internal static class DispatcherReports
{
    private static readonly ConcurrentQueue<DispatcherReport> s_reports = new();

    public static void Add(DispatcherReport report) => s_reports.Enqueue(report);

    public static DispatcherReport[] Of(DispatcherReportKind kind, string name)
    {
        return [.. s_reports.Where(report => report.Kind == kind && report.Name == name)];
    }
}
