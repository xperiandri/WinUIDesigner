// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;

namespace WinUIDesigner.Surface.Common;

/// <summary>
/// What the surface does around a pass over the remembered <c>{ThemeResource}</c> properties
/// (<see cref="ThemeResourceRegistry.Reapply"/>). WinUI-free: the pass, the completion and the report come through
/// delegates.
/// </summary>
internal static class ThemeResourcePass
{
    /// <summary>
    /// Runs <paramref name="reapply"/> and, when it gave a property a new value, <paramref name="complete"/> (the layout
    /// and the publication of the result). The new values are in place whatever the completion does, so a completion that
    /// throws is handed to <paramref name="onCompletionFailure"/> and not thrown: the request that asked for the pass has
    /// succeeded.
    /// </summary>
    /// <returns><see langword="true"/> when <paramref name="reapply"/> gave a property a new value.</returns>
    public static bool ReapplyThenComplete(Func<bool> reapply, Action complete, Action<Exception> onCompletionFailure)
    {
        // No property got a new value: nothing to lay out again.
        if (!reapply())
        {
            return false;
        }

        // A property got a new value: lay out again and tell Visual Studio.
        try
        {
            complete();
        }
        catch (Exception exception)
        {
            // The values are applied: a layout that fails after them is reported, and the request is not failed with it.
            onCompletionFailure(exception);
        }

        return true;
    }
}
