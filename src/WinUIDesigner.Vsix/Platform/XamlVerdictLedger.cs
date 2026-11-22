// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;

namespace WinUIDesigner.Platform;

/// <summary>
/// Remembers which places of a document were dropped when the preview of the document was last in step with it,
/// and tells which places an edit has put on the other side.
/// </summary>
/// <typeparam name="TPlace">A place in a document: an element, or a property of one.</typeparam>
/// <remarks>
/// <para>
/// A place is dropped when it was written under a prefix that a namespace policy removes. Whether it is dropped
/// depends on its text alone: Visual Studio's incremental update of the document model notices no change when only
/// the prefix of an element or an attribute changes, or the condition of a declaration, and builds nothing. The
/// preview keeps showing what it showed, and the edits that follow are dropped or land on a neighbour.
/// </para>
/// <para>
/// Only dropping matters: a place that is kept and a place that is left as written are both built.
/// </para>
/// </remarks>
internal sealed class XamlVerdictLedger<TPlace>
    where TPlace : notnull
{
    private Dictionary<TPlace, bool>? _dropped;
    private bool _hadPolicyNamespaces;

    /// <summary>
    /// Records the places of a document as they are now.
    /// </summary>
    /// <param name="look">Returns, for every place of the document, whether it is dropped. Called only when it is needed.</param>
    /// <param name="hasPolicyNamespaces">Whether the document declares a namespace that a policy may remove members of.</param>
    /// <returns>
    /// The places that were not dropped and are now, or were and are not: the places the preview has to be built
    /// again for. It is empty the first time a document is looked at, because there is nothing to compare with.
    /// </returns>
    public IReadOnlyList<TPlace> Update(Func<IReadOnlyDictionary<TPlace, bool>> look, bool hasPolicyNamespaces)
    {
        // Nothing is dropped, and nothing was.
        if (!hasPolicyNamespaces && !_hadPolicyNamespaces)
        {
            _dropped = [];
            return [];
        }

        IReadOnlyDictionary<TPlace, bool> current = look();
        Dictionary<TPlace, bool>? previous = _dropped;
        bool hadPolicyNamespaces = _hadPolicyNamespaces;

        // What is kept is a copy: the caller may go on using what it returned.
        var remembered = new Dictionary<TPlace, bool>(current.Count);
        foreach (KeyValuePair<TPlace, bool> place in current)
        {
            remembered[place.Key] = place.Value;
        }

        _dropped = remembered;
        _hadPolicyNamespaces = hasPolicyNamespaces;

        // The first look is the state the preview was built from.
        if (previous is null)
        {
            return [];
        }

        var changed = new List<TPlace>();
        foreach (KeyValuePair<TPlace, bool> place in current)
        {
            if (previous.TryGetValue(place.Key, out bool wasDropped))
            {
                // A place that was there before is built as it was judged then.
                if (wasDropped != place.Value)
                {
                    changed.Add(place.Key);
                }
            }
            else if (!hadPolicyNamespaces && place.Value)
            {
                // Nothing was dropped in a document without policy namespaces, so a place that is dropped now
                // may have been built: the places that were there are not known, and are all taken for it.
                changed.Add(place.Key);
            }
        }

        return changed;
    }

    /// <summary>
    /// Forgets what is known of the document, so that the next look is a first one.
    /// </summary>
    public void Reset()
    {
        _dropped = null;
        _hadPolicyNamespaces = false;
    }
}
