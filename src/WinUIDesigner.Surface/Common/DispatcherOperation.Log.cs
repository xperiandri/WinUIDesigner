// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;

namespace WinUIDesigner.Surface.Common;

/// <summary>
/// Writes the reports of <see cref="DispatcherOperation"/> to the surface log. This part is not compiled into the tests,
/// which record the reports with their own implementation of these methods.
/// </summary>
internal static partial class DispatcherOperation
{
    static partial void ReportStep(string step, TimeSpan queueWait, TimeSpan runTime)
    {
        WinUIDesignerLogger.LogTrace("Surface", $"'{step}' waited {queueWait.TotalMilliseconds:0} ms for the UI thread and ran for {runTime.TotalMilliseconds:0} ms.");
    }

    static partial void ReportGaveUp(string request, TimeSpan waited, Exception reason)
    {
        if (reason is TimeoutException)
        {
            // The time limit passed: the UI thread was busy for that long.
            WinUIDesignerLogger.LogWarning("Surface", $"'{request}' gave up after {waited.TotalSeconds:0.#} s: the UI thread did not take it in time.");
        }
        else
        {
            // Cancelled or refused: the protocol or the dispatcher queue is shutting down.
            WinUIDesignerLogger.LogInformation("Surface", $"'{request}' gave up after {waited.TotalSeconds:0.#} s: {reason.Message}");
        }
    }

    static partial void ReportLateFinish(string request, TimeSpan late, Exception? exception)
    {
        WinUIDesignerLogger.LogWarning("Surface", $"'{request}' finished {late.TotalSeconds:0.#} s after it gave up; its result was discarded.", exception);
    }

    static partial void ReportLongWait(string what, TimeSpan waited)
    {
        WinUIDesignerLogger.LogWarning("Surface", $"'{what}' took {waited.TotalSeconds:0.#} s.");
    }
}
