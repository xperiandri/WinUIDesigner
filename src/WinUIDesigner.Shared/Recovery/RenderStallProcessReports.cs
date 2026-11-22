// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;

namespace WinUIDesigner.Recovery;

/// <summary>
/// The render stalls that one surface process reported. The surface reports a stall for every document that it shows,
/// also after its first report, and the process is restarted once. A report that is not followed by a restart leaves
/// the next one to be decided again: only a restart brings such a process back, and a reload of the designer reports
/// the stall again from the same process.
/// </summary>
internal sealed class RenderStallProcessReports
{
    private bool _restarting;
    private RenderStallRestart? _refusal;

    /// <summary>
    /// What to do about an unhandled-exception message of the process: null when there is nothing to do, or nothing new
    /// to say.
    /// </summary>
    public RenderStallRestart? Decide(RenderStallRestartPolicy policy, string? message, DateTime now, bool contextDisposed)
    {
        // Not a render stall, a process that is being restarted already, or a context that can restart nothing any more.
        if (!RenderStallReport.IsReport(message) || _restarting || contextDisposed)
        {
            return null;
        }

        // The first report that the policy allows restarts the process.
        RenderStallRestart decision = policy.Decide(now);
        if (decision == RenderStallRestart.Restart)
        {
            _restarting = true;
            return decision;
        }

        // A refusal is said once for the process; the reports after it are decided again all the same.
        if (decision == _refusal)
        {
            return null;
        }

        _refusal = decision;
        return decision;
    }
}
