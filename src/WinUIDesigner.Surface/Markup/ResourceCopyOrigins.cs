// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Xml.Linq;

namespace WinUIDesigner.Surface.Markup;

/// <summary>
/// Remembers which prepared dictionary each inserted copy of a dictionary in the text of a document is a copy of.
/// </summary>
/// <remarks>
/// <para>
/// The same prepared dictionary is usually in the text of a document more than once: an application merges the
/// <c>Resources.xaml</c> of each of its libraries, and each of them merges the same shared dictionary. A resource that
/// the reader rejects in one copy is rejected in all of them, and the origins say which copies are the same.
/// </para>
/// <para>
/// A copy is an element that LINQ to XML duplicated from the template of the prepared dictionary, and a duplicate
/// does not carry annotations over, so the origin cannot be an annotation: the copies nested in a template would lose
/// theirs the next time the template is copied. The origins are a side table keyed by the elements instead, filled
/// where a template is copied (<see cref="CopyTemplate"/>). Its keys are weak: the origin of an element goes with it.
/// </para>
/// </remarks>
internal sealed class ResourceCopyOrigins
{
    private readonly ConditionalWeakTable<XElement, ResourceCopyOrigin> _origins = new();

    /// <summary>
    /// Records that <paramref name="copy"/>, the root element of a dictionary, comes from <paramref name="origin"/>.
    /// </summary>
    public void Add(XElement copy, ResourceCopyOrigin origin) => _origins.AddOrUpdate(copy, origin);

    /// <summary>
    /// Duplicates <paramref name="template"/> and carries the origins of its elements over to the duplicate: the
    /// origin of the template itself and that of every dictionary nested in it.
    /// </summary>
    public XElement CopyTemplate(XElement template)
    {
        var copy = new XElement(template);

        // A duplicate has the structure of the original, so both are walked in step.
        using IEnumerator<XElement> originals = template.DescendantsAndSelf().GetEnumerator();
        using IEnumerator<XElement> duplicates = copy.DescendantsAndSelf().GetEnumerator();
        while (originals.MoveNext() && duplicates.MoveNext())
        {
            if (_origins.TryGetValue(originals.Current, out ResourceCopyOrigin? origin))
            {
                _origins.AddOrUpdate(duplicates.Current, origin);
            }
        }

        return copy;
    }

    /// <summary>
    /// Returns the copy of a prepared dictionary that <paramref name="element"/> is in: the nearest element at or
    /// above it that is the root of such a copy.
    /// </summary>
    /// <returns>
    /// <see langword="false"/> for an element that belongs to the document itself and to no prepared copy, and for
    /// one inside a dictionary that the runtime loads by its URI.
    /// </returns>
    public bool TryFindCopy(XElement element, [NotNullWhen(true)] out XElement? copy, [NotNullWhen(true)] out ResourceCopyOrigin? origin)
    {
        foreach (XElement candidate in element.AncestorsAndSelf())
        {
            if (_origins.TryGetValue(candidate, out origin))
            {
                copy = candidate;
                return !origin.IsRuntime;
            }
        }

        copy = null;
        origin = null;
        return false;
    }

    /// <summary>
    /// Lists the copies of dictionaries in the tree under <paramref name="root"/>, in document order.
    /// </summary>
    public IEnumerable<KeyValuePair<XElement, ResourceCopyOrigin>> FindCopies(XElement root)
    {
        foreach (XElement element in root.DescendantsAndSelf())
        {
            if (_origins.TryGetValue(element, out ResourceCopyOrigin? origin))
            {
                yield return new KeyValuePair<XElement, ResourceCopyOrigin>(element, origin);
            }
        }
    }
}

/// <summary>
/// Where a copy of a dictionary comes from.
/// </summary>
/// <param name="Path">
/// The full path of the prepared copy that Visual Studio wrote, or <see langword="null"/> for a dictionary that is
/// merged with its source, for the runtime to load.
/// </param>
/// <param name="Name">The name that messages use for the dictionary.</param>
/// <param name="Uri">The URI the dictionary is named by.</param>
internal sealed record ResourceCopyOrigin(string? Path, string Name, string Uri)
{
    /// <summary>
    /// Gets whether the dictionary is merged with its source for the runtime to load.
    /// </summary>
    public bool IsRuntime => Path is null;
}
