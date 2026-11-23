// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace WinUIDesigner.Surface.Common;

/// <summary>
/// Moves the holders of a reference to a replaced value over to the value that replaced it.
/// </summary>
/// <remarks>
/// The edit that replaced the value is already applied when its references move, so moving them is best effort: a
/// holder that cannot take the new value keeps the old one, as before the edit, the failure is reported, and the other
/// holders still move. Holders can also be tracked, so that they move wherever they are.
/// </remarks>
/// <typeparam name="THolder">An object that refers to a value through one of its properties.</typeparam>
/// <typeparam name="TValue">The value referred to.</typeparam>
/// <param name="read">Returns the value a holder refers to.</param>
/// <param name="accepts">Returns whether a holder can refer to a new value.</param>
/// <param name="write">Makes a holder refer to a new value.</param>
/// <param name="failed">Reports a holder that could not move, or holders that could not be listed.</param>
internal sealed class ReferenceMover<THolder, TValue>(
    Func<THolder, TValue?> read,
    Func<THolder, TValue, bool> accepts,
    Action<THolder, TValue> write,
    Action<Exception> failed)
    where THolder : class
    where TValue : class
{
    private readonly ConditionalWeakTable<THolder, object?> _tracked = new();

    /// <summary>
    /// Remembers <paramref name="holder"/>, without keeping it alive, so that <see cref="MoveTracked"/> moves it
    /// wherever it is.
    /// </summary>
    public void Track(THolder holder) => _tracked.AddOrUpdate(holder, null);

    /// <summary>
    /// Moves every tracked holder that refers to <paramref name="oldValue"/>.
    /// </summary>
    public void MoveTracked(TValue oldValue, TValue newValue) => MoveAll(_tracked.Select(static entry => entry.Key), oldValue, newValue);

    /// <summary>
    /// Moves <paramref name="holder"/> when it refers to <paramref name="oldValue"/>.
    /// </summary>
    public void Move(THolder holder, TValue oldValue, TValue newValue)
    {
        try
        {
            if (ReferenceEquals(read(holder), oldValue) && accepts(holder, newValue))
            {
                write(holder, newValue);
            }
        }
        catch (Exception ex)
        {
            failed(ex);
        }
    }

    /// <summary>
    /// Moves every holder in <paramref name="holders"/> that refers to <paramref name="oldValue"/>. A failure while the
    /// holders are listed ends this list; the holders listed before it have moved.
    /// </summary>
    public void MoveAll(IEnumerable<THolder> holders, TValue oldValue, TValue newValue)
    {
        try
        {
            foreach (THolder holder in holders)
            {
                Move(holder, oldValue, newValue);
            }
        }
        catch (Exception ex)
        {
            failed(ex);
        }
    }
}
