// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;

namespace WinUIDesigner.Surface.Common;

/// <summary>
/// Holds child insertions that have to wait until the inserted object is complete, in their original order.
/// </summary>
/// <remarks>
/// Visual Studio adds a Setter to its collection before it sets the setter's Property and Value. WinUI rejects
/// an incomplete setter, and inserting it through XAML Diagnostics would skip the merged setters of styles
/// BasedOn the edited one, so the insertion waits. Visual Studio computes every index of a batch against the
/// collection as it would be after the earlier actions; another operation on the same collection therefore
/// takes its pending insertions first. By then the setters are complete: Visual Studio sets a setter's
/// properties right after adding it.
/// </remarks>
/// <typeparam name="TEntry">The pending insertion.</typeparam>
/// <param name="getTarget">Returns the collection an entry inserts into.</param>
internal sealed class DeferredInsertionQueue<TEntry>(Func<TEntry, object> getTarget)
{
    private readonly List<TEntry> _entries = [];

    public int Count => _entries.Count;

    public void Add(TEntry entry) => _entries.Add(entry);

    /// <summary>
    /// Counts the pending insertions into <paramref name="target"/>.
    /// </summary>
    public int CountFor(object target)
    {
        int count = 0;
        foreach (TEntry entry in _entries)
        {
            if (ReferenceEquals(getTarget(entry), target))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Removes and returns the pending insertions into <paramref name="target"/>, oldest first.
    /// </summary>
    /// <remarks>
    /// The surface takes before every GetChild, RemoveChild and ClearCollectionProperty, whatever the collection is,
    /// and while a document is built the queue holds every setter of the document. Most calls therefore find nothing,
    /// and then the queue is left as it is.
    /// </remarks>
    public List<TEntry> TakeFor(object target)
    {
        var taken = new List<TEntry>();
        foreach (TEntry entry in _entries)
        {
            if (ReferenceEquals(getTarget(entry), target))
            {
                taken.Add(entry);
            }
        }

        if (taken.Count != 0)
        {
            RemoveEntriesFor(target);
        }

        return taken;
    }

    // A method of its own: a lambda that captured the target inside TakeFor would allocate its closure on every call.
    private void RemoveEntriesFor(object target) => _entries.RemoveAll(entry => ReferenceEquals(getTarget(entry), target));

    /// <summary>
    /// Inserts the pending entries for <paramref name="target"/>, oldest first, and then runs
    /// <paramref name="operation"/>, which addresses the items of the target by index.
    /// </summary>
    /// <param name="target">The collection the operation addresses; nothing is pending for <see langword="null"/>.</param>
    /// <param name="insert">Applies one pending insertion.</param>
    /// <param name="operation">Removes, reads or clears items of the target.</param>
    public void RunAfterPendingInsertions(object? target, Action<TEntry> insert, Action operation)
    {
        if (target is not null)
        {
            foreach (TEntry entry in TakeFor(target))
            {
                insert(entry);
            }
        }

        operation();
    }

    /// <summary>
    /// Removes and returns every pending insertion, oldest first.
    /// </summary>
    public List<TEntry> TakeAll()
    {
        var taken = new List<TEntry>(_entries);
        _entries.Clear();
        return taken;
    }
}
