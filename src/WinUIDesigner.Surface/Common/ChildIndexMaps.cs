// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace WinUIDesigner.Surface.Common;

/// <summary>
/// Keeps a <see cref="ChildIndexMap"/> for each live collection that has dropped elements.
/// </summary>
/// <remarks>
/// A collection is identified by reference, as the object Visual Studio's actions resolve to. A collection without a
/// map has no dropped elements: its indices are used as they are, exactly as before maps existed. A map is created
/// when the first dropped element of a collection is recorded, from a construction action or from a loaded document.
/// The maps hold their collections, so the runtime keeps handing out the same object for a collection that has one.
/// </remarks>
internal sealed class ChildIndexMaps
{
    private readonly Dictionary<object, ChildIndexMap> _maps = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Gets the number of collections with a map.
    /// </summary>
    public int Count => _maps.Count;

    /// <summary>
    /// Returns the map of <paramref name="collection"/>, if it has one.
    /// </summary>
    public bool TryGet(object collection, [NotNullWhen(true)] out ChildIndexMap? map)
    {
        ArgumentNullException.ThrowIfNull(collection);
        return _maps.TryGetValue(collection, out map);
    }

    /// <summary>
    /// Replaces the map of <paramref name="collection"/> with one that has holes at <paramref name="holes"/>.
    /// A loaded document registers the elements it dropped this way.
    /// </summary>
    /// <returns>The new map.</returns>
    public ChildIndexMap Register(object collection, IEnumerable<int> holes)
    {
        ArgumentNullException.ThrowIfNull(collection);
        var map = new ChildIndexMap(holes);
        _maps[collection] = map;
        return map;
    }

    /// <summary>
    /// Records a dropped element that Visual Studio inserted into <paramref name="collection"/> at <paramref name="modelIndex"/>.
    /// </summary>
    /// <param name="collection">The live collection.</param>
    /// <param name="modelIndex">The index Visual Studio inserts at.</param>
    /// <param name="liveCount">The number of objects in the live collection.</param>
    /// <returns>The map of the collection, created on the first dropped element.</returns>
    public ChildIndexMap InsertHole(object collection, int modelIndex, int liveCount)
    {
        ArgumentNullException.ThrowIfNull(collection);
        if (!_maps.TryGetValue(collection, out ChildIndexMap? map))
        {
            _maps[collection] = map = new ChildIndexMap();
        }

        map.InsertHole(modelIndex, liveCount);
        return map;
    }

    /// <summary>
    /// Forgets the dropped elements of <paramref name="collection"/>, for example because it was cleared.
    /// </summary>
    public bool Reset(object collection)
    {
        ArgumentNullException.ThrowIfNull(collection);
        return _maps.Remove(collection);
    }

    /// <summary>
    /// Forgets the maps of the collections in <paramref name="released"/>, the objects of a released document.
    /// </summary>
    public void Release(IEnumerable<object> released)
    {
        ArgumentNullException.ThrowIfNull(released);
        foreach (object value in released)
        {
            _maps.Remove(value);
        }
    }

    /// <summary>
    /// Forgets every map.
    /// </summary>
    public void Clear()
    {
        _maps.Clear();
    }
}
