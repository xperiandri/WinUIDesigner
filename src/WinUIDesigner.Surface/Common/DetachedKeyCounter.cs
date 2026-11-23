// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System.Collections.Generic;

namespace WinUIDesigner.Surface.Common;

/// <summary>
/// Counts, per resource key, the removals through XAML Diagnostics that no addition through it has paired with yet.
/// </summary>
/// <remarks>
/// A removal through XAML Diagnostics detaches the <c>{StaticResource}</c> references that the framework recorded for
/// the key, and only an addition through it resolves them again: in the same batch when Visual Studio rebuilds a style,
/// in another dictionary when the style moves there, or in a later batch when the removal is undone. Each removal pairs
/// with one such addition. Other additions under the key keep the public API, because XAML Diagnostics keeps every
/// object it is given alive until the process exits.
/// </remarks>
internal sealed class DetachedKeyCounter
{
    // Keys are strings or types; both compare by value.
    private readonly Dictionary<object, int> _unpairedRemovals = [];

    /// <summary>
    /// Records a removal of <paramref name="key"/> through XAML Diagnostics.
    /// </summary>
    public void Removed(object key) => _unpairedRemovals[key] = _unpairedRemovals.GetValueOrDefault(key) + 1;

    /// <summary>
    /// Pairs an addition under <paramref name="key"/> with a removal through XAML Diagnostics. Returns
    /// <see langword="false"/> when every such removal of the key has paired already: the addition then keeps the
    /// public API.
    /// </summary>
    public bool TryPairAddition(object key)
    {
        if (!_unpairedRemovals.TryGetValue(key, out int count))
        {
            return false;
        }

        if (count == 1)
        {
            _unpairedRemovals.Remove(key);
        }
        else
        {
            _unpairedRemovals[key] = count - 1;
        }

        return true;
    }
}
