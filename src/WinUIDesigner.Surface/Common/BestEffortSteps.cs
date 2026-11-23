// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;

namespace WinUIDesigner.Surface.Common;

/// <summary>
/// Runs the steps of an operation that follows an edit that is applied already, each guarded on its own.
/// </summary>
/// <remarks>
/// What follows an applied edit is best effort: a step that fails is reported, and the steps after it still run,
/// instead of the failure ending the operation and keeping the steps that would have worked from running.
/// </remarks>
/// <param name="failed">Reports a step that failed.</param>
internal sealed class BestEffortSteps(Action<Exception> failed)
{
    /// <summary>
    /// Runs <paramref name="step"/>. A failure is reported and does not escape.
    /// </summary>
    public void Run(Action step)
    {
        try
        {
            step();
        }
        catch (Exception ex)
        {
            failed(ex);
        }
    }
}
