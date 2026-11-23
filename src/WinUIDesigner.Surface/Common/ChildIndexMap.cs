// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;

namespace WinUIDesigner.Surface.Common;

/// <summary>
/// What an index of Visual Studio's document model refers to in a live collection.
/// </summary>
internal enum ChildSlotKind
{
    /// <summary>
    /// An object of the live collection.
    /// </summary>
    Live,

    /// <summary>
    /// An element a namespace policy dropped: the document model has it, the live collection does not.
    /// </summary>
    Hole,

    /// <summary>
    /// Nothing: the index is outside the document model's view of the collection.
    /// </summary>
    Outside,
}

/// <summary>
/// The result of looking up an index of Visual Studio's document model in a <see cref="ChildIndexMap"/>.
/// </summary>
internal readonly struct ChildSlot : IEquatable<ChildSlot>
{
    private ChildSlot(ChildSlotKind kind, int liveIndex)
    {
        Kind = kind;
        LiveIndex = liveIndex;
    }

    /// <summary>
    /// Gets the slot of a dropped element.
    /// </summary>
    public static ChildSlot Hole => new(ChildSlotKind.Hole, -1);

    /// <summary>
    /// Gets the slot of an index that refers to nothing.
    /// </summary>
    public static ChildSlot Outside => new(ChildSlotKind.Outside, -1);

    /// <summary>
    /// Gets what the index refers to.
    /// </summary>
    public ChildSlotKind Kind { get; }

    /// <summary>
    /// Gets the index in the live collection, or -1 when <see cref="Kind"/> is not <see cref="ChildSlotKind.Live"/>.
    /// </summary>
    public int LiveIndex { get; }

    /// <summary>
    /// Returns the slot of the object at <paramref name="liveIndex"/> in the live collection.
    /// </summary>
    public static ChildSlot Live(int liveIndex) => new(ChildSlotKind.Live, liveIndex);

    public bool Equals(ChildSlot other) => Kind == other.Kind && LiveIndex == other.LiveIndex;

    public override bool Equals(object? obj) => obj is ChildSlot other && Equals(other);

    public override int GetHashCode() => ((int)Kind * 397) ^ LiveIndex;

    public override string ToString() => Kind == ChildSlotKind.Live ? $"Live({LiveIndex})" : Kind.ToString();
}

/// <summary>
/// Translates the child indices Visual Studio uses for one collection into indices of the live collection.
/// </summary>
/// <remarks>
/// <para>
/// Visual Studio addresses the children of a collection by their index in its document model. That model still
/// contains the elements a namespace policy dropped, such as the branch of a conditional namespace that does not
/// apply; the live collection does not. The map keeps the model positions of those elements, the holes, so that an
/// index of the model can be turned into an index of the live collection.
/// </para>
/// <para>
/// Visual Studio sends its child actions in an order that is consistent when they are applied one after the other.
/// Each operation of the map applies one of them to the model's view of the collection: an insertion moves the holes
/// at and after its position by one, a removal moves the holes after it back. The live collection holds the entries
/// of the model's view that are not holes, in the same order. Every operation takes the current number of objects in
/// the live collection, so that the map cannot point past the end even when the collection changed behind its back.
/// </para>
/// </remarks>
internal sealed class ChildIndexMap
{
    // The model positions of the holes, ascending and distinct.
    private readonly List<int> _holes = [];

    /// <summary>
    /// Creates a map without holes.
    /// </summary>
    public ChildIndexMap()
    {
    }

    /// <summary>
    /// Creates a map with holes at the given model positions, as a loaded document has them.
    /// </summary>
    /// <param name="holes">The model positions of the dropped elements; negative positions and duplicates are ignored.</param>
    public ChildIndexMap(IEnumerable<int> holes)
    {
        ArgumentNullException.ThrowIfNull(holes);

        foreach (int hole in holes)
        {
            // A position before the collection refers to nothing.
            if (hole < 0)
            {
                continue;
            }

            // A position that is there already is kept once; the others go in ascending order.
            int index = _holes.BinarySearch(hole);
            if (index < 0)
            {
                _holes.Insert(~index, hole);
            }
        }
    }

    /// <summary>
    /// Gets the model positions of the holes in ascending order.
    /// </summary>
    public IReadOnlyList<int> Holes => _holes;

    /// <summary>
    /// Records that a dropped element was inserted at <paramref name="modelIndex"/>.
    /// </summary>
    /// <param name="modelIndex">The index Visual Studio inserts at. An index past the end appends.</param>
    /// <param name="liveCount">The number of objects in the live collection.</param>
    /// <returns>The model position the hole was recorded at.</returns>
    public int InsertHole(int modelIndex, int liveCount)
    {
        Normalize(liveCount);

        int position = ClampInsertion(modelIndex, liveCount);
        int first = CountHolesBefore(position);
        Shift(first, 1);
        _holes.Insert(first, position);
        return position;
    }

    /// <summary>
    /// Records that an object was inserted at <paramref name="modelIndex"/> and returns where it goes in the live collection.
    /// </summary>
    /// <param name="modelIndex">The index Visual Studio inserts at. An index past the end appends.</param>
    /// <param name="liveCount">The number of objects in the live collection before the insertion.</param>
    /// <returns>The index to insert at in the live collection, from 0 to <paramref name="liveCount"/>.</returns>
    public int InsertItem(int modelIndex, int liveCount)
    {
        Normalize(liveCount);

        int position = ClampInsertion(modelIndex, liveCount);
        int holesBefore = CountHolesBefore(position);
        Shift(holesBefore, 1);
        return position - holesBefore;
    }

    /// <summary>
    /// Records that the entry at <paramref name="modelIndex"/> was removed and returns what it was.
    /// </summary>
    /// <param name="modelIndex">The index Visual Studio removes.</param>
    /// <param name="liveCount">The number of objects in the live collection before the removal.</param>
    /// <returns>
    /// The live index of the object to remove; <see cref="ChildSlot.Hole"/> when a dropped element was removed, which
    /// leaves the live collection as it is; <see cref="ChildSlot.Outside"/> when there is no such entry, which changes nothing.
    /// </returns>
    public ChildSlot RemoveAt(int modelIndex, int liveCount)
    {
        Normalize(liveCount);

        // No such entry in the model's view of the collection.
        if (modelIndex < 0 || modelIndex >= liveCount + _holes.Count)
        {
            return ChildSlot.Outside;
        }

        // A dropped element: the hole goes, and the holes after it move down.
        int index = _holes.BinarySearch(modelIndex);
        if (index >= 0)
        {
            _holes.RemoveAt(index);
            Shift(index, -1);
            return ChildSlot.Hole;
        }

        // An object: its live index is its model index minus the holes before it, and the holes after it move down.
        int holesBefore = ~index;
        Shift(holesBefore, -1);
        return ChildSlot.Live(modelIndex - holesBefore);
    }

    /// <summary>
    /// Returns what the entry at <paramref name="modelIndex"/> is, without changing anything.
    /// </summary>
    /// <param name="modelIndex">The index Visual Studio looks up.</param>
    /// <param name="liveCount">The number of objects in the live collection.</param>
    public ChildSlot Find(int modelIndex, int liveCount)
    {
        Normalize(liveCount);

        // No such entry in the model's view of the collection.
        if (modelIndex < 0 || modelIndex >= liveCount + _holes.Count)
        {
            return ChildSlot.Outside;
        }

        // A dropped element, or an object at its model index minus the holes before it.
        int index = _holes.BinarySearch(modelIndex);
        return index >= 0 ? ChildSlot.Hole : ChildSlot.Live(modelIndex - ~index);
    }

    /// <summary>
    /// Returns a short description of the holes for logging.
    /// </summary>
    public override string ToString() => $"[{string.Join(", ", _holes)}]";

    /// <summary>
    /// Returns the position an insertion goes to: the index itself, or the end for an index that is negative or past it.
    /// </summary>
    private int ClampInsertion(int modelIndex, int liveCount)
    {
        int count = liveCount + _holes.Count;
        return modelIndex < 0 || modelIndex > count ? count : modelIndex;
    }

    private int CountHolesBefore(int position)
    {
        int index = _holes.BinarySearch(position);
        return index >= 0 ? index : ~index;
    }

    private void Shift(int first, int delta)
    {
        for (int index = first; index < _holes.Count; index++)
        {
            _holes[index] += delta;
        }
    }

    /// <summary>
    /// Keeps every hole within the model's view of a collection of <paramref name="liveCount"/> objects.
    /// </summary>
    /// <remarks>
    /// At most <paramref name="liveCount"/> objects can precede a hole, so the k-th hole cannot lie after position
    /// <c>liveCount + k</c>. A collection that lost objects without Visual Studio's knowledge would otherwise leave holes
    /// past its end, and an index that refers to an object could then translate to a live index past the end.
    /// Clamped holes stay distinct and ascending: they become the last positions of the view.
    /// </remarks>
    private void Normalize(int liveCount)
    {
        int count = Math.Max(liveCount, 0);
        for (int index = 0; index < _holes.Count; index++)
        {
            int limit = count + index;
            if (_holes[index] > limit)
            {
                _holes[index] = limit;
            }
        }
    }
}
