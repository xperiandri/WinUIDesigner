// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml.Linq;

namespace WinUIDesigner.Surface.Markup;

/// <summary>
/// A collection of a prepared document that lost elements to the namespace policies.
/// </summary>
/// <param name="Mark">The value of the <c>DT.DroppedChildOwner</c> mark of the owner.</param>
/// <param name="Owner">The element that holds the collection.</param>
/// <param name="Property">The property element of the collection, or <see langword="null"/> for content.</param>
/// <param name="Holes">The positions of the dropped elements in Visual Studio's document model, ascending.</param>
internal sealed record DroppedChildCollection(string Mark, XElement Owner, XName? Property, List<int> Holes);

/// <summary>
/// Marks, in prepared XAML, the elements that hold a collection the namespace policies removed elements from.
/// </summary>
/// <remarks>
/// The XAML reader creates an object for every element, but nothing ties the object back to its element once the
/// document is loaded; for an element outside the visual tree, such as a style or a resource dictionary, the source
/// mapping does not reach it either. The mark is the attached property <c>DT.DroppedChildOwner</c>, which the reader
/// sets on the object it creates, so the surface hears of every marked object during the load.
/// </remarks>
internal static class DroppedChildOwnerMarks
{
    /// <summary>
    /// The name of the mark: the attached property <c>DT.DroppedChildOwner</c> in the namespace of the surface types.
    /// </summary>
    public static readonly XName MarkName = XName.Get("DT.DroppedChildOwner", "using:XSurfUwp");

    /// <summary>
    /// Marks the owners of the collections in <paramref name="droppedChildren"/> within <paramref name="runtimeRoot"/>.
    /// </summary>
    /// <param name="runtimeRoot">The element that is loaded.</param>
    /// <param name="droppedChildren">What the rewrite removed, from <see cref="XamlNamespaceRewriteResult.DroppedChildren"/>.</param>
    /// <returns>The collections with their holes, in document order.</returns>
    /// <remarks>
    /// Elements outside the loaded element are left alone, and so is template content: the reader does not create it
    /// while it loads the document.
    /// </remarks>
    public static List<DroppedChildCollection> Mark(XElement runtimeRoot, IReadOnlyList<XamlDroppedChild> droppedChildren)
    {
        ArgumentNullException.ThrowIfNull(runtimeRoot);
        ArgumentNullException.ThrowIfNull(droppedChildren);

        var collections = new List<DroppedChildCollection>();
        var marks = new Dictionary<XElement, string>();
        foreach (XamlDroppedChild dropped in droppedChildren)
        {
            if (!IsLoadedWith(dropped.Owner, runtimeRoot))
            {
                continue;
            }

            if (!marks.TryGetValue(dropped.Owner, out string? mark))
            {
                mark = marks.Count.ToString(CultureInfo.InvariantCulture);
                marks.Add(dropped.Owner, mark);
                dropped.Owner.SetAttributeValue(MarkName, mark);
            }

            DroppedChildCollection? collection = collections.Find(candidate => ReferenceEquals(candidate.Owner, dropped.Owner) && candidate.Property == dropped.Property);
            if (collection is null)
            {
                collections.Add(collection = new DroppedChildCollection(mark, dropped.Owner, dropped.Property, []));
            }

            collection.Holes.Add(dropped.Index);
        }

        return collections;
    }

    /// <summary>
    /// Removes the marks <see cref="Mark"/> added, so that the document can be loaded as it was prepared.
    /// </summary>
    /// <remarks>
    /// The XAML reader can only set the mark on a <c>DependencyObject</c>; on any other object it fails the whole
    /// document. Loading it again without marks gives the document as it was before marks existed.
    /// </remarks>
    public static void Unmark(IEnumerable<DroppedChildCollection> collections)
    {
        ArgumentNullException.ThrowIfNull(collections);

        foreach (DroppedChildCollection collection in collections)
        {
            collection.Owner.Attribute(MarkName)?.Remove();
        }
    }

    /// <summary>
    /// Returns whether the XAML reader creates <paramref name="owner"/> when it loads <paramref name="runtimeRoot"/>:
    /// it has to be part of that element and outside template content.
    /// </summary>
    private static bool IsLoadedWith(XElement owner, XElement runtimeRoot)
    {
        for (XElement? current = owner; current is not null; current = current.Parent)
        {
            if (current.Name.LocalName is "ControlTemplate" or "DataTemplate" or "ItemsPanelTemplate")
            {
                return false;
            }

            if (ReferenceEquals(current, runtimeRoot))
            {
                return true;
            }
        }

        return false;
    }
}
