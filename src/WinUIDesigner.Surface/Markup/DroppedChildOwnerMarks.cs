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
    /// <param name="canCarryMark">
    /// Tells whether the object the reader creates for an element can carry the mark: whether it is a
    /// <c>DependencyObject</c>. <see langword="null"/> when every element can.
    /// </param>
    /// <returns>The collections with their holes, in document order.</returns>
    /// <remarks>
    /// <para>
    /// Elements outside the loaded element are left alone, and so is template content: the reader does not create it
    /// while it loads the document.
    /// </para>
    /// <para>
    /// The reader fails the whole document when it is asked to mark an object that is no <c>DependencyObject</c>, so
    /// such an element is not marked. An explicit collection element, such as <c>&lt;TransitionCollection&gt;</c> in
    /// <c>&lt;Grid.ChildrenTransitions&gt;</c>, is the value of a property of the element around it, and that element
    /// carries the mark for the collection. Any other element that cannot carry the mark goes without holes, and only
    /// its collection does.
    /// </para>
    /// </remarks>
    public static List<DroppedChildCollection> Mark(XElement runtimeRoot, IReadOnlyList<XamlDroppedChild> droppedChildren, Func<XElement, bool>? canCarryMark = null)
    {
        ArgumentNullException.ThrowIfNull(runtimeRoot);
        ArgumentNullException.ThrowIfNull(droppedChildren);

        var collections = new List<DroppedChildCollection>();
        var marks = new Dictionary<XElement, string>();
        foreach (XamlDroppedChild dropped in droppedChildren)
        {
            if (!TryGetCarrier(dropped, canCarryMark, out XElement owner, out XName? property) || !IsLoadedWith(owner, runtimeRoot))
            {
                continue;
            }

            if (!marks.TryGetValue(owner, out string? mark))
            {
                mark = marks.Count.ToString(CultureInfo.InvariantCulture);
                marks.Add(owner, mark);
                owner.SetAttributeValue(MarkName, mark);
            }

            DroppedChildCollection? collection = collections.Find(candidate => ReferenceEquals(candidate.Owner, owner) && candidate.Property == property);
            if (collection is null)
            {
                collections.Add(collection = new DroppedChildCollection(mark, owner, property, []));
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
    /// document. <see cref="Mark"/> leaves out the elements it knows to be none; one whose type it could not tell
    /// is still marked, and loading the document again without marks gives it as it was before marks existed.
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
    /// Finds the element that carries the mark for the collection <paramref name="dropped"/> was removed from, and the
    /// property element of the collection on it.
    /// </summary>
    /// <returns><see langword="false"/> when no element can carry the mark for the collection.</returns>
    private static bool TryGetCarrier(XamlDroppedChild dropped, Func<XElement, bool>? canCarryMark, out XElement owner, out XName? property)
    {
        owner = dropped.Owner;
        property = dropped.Property;
        if (canCarryMark is null || canCarryMark(owner))
        {
            return true;
        }

        // The owner is no DependencyObject. It can still be the explicit collection of a property of one:
        // <Grid.ChildrenTransitions><TransitionCollection>...</TransitionCollection></Grid.ChildrenTransitions>.
        if (property is not null || owner.Parent is not { } propertyElement || !IsPropertyElement(propertyElement))
        {
            return false;
        }

        // The object that holds the property carries the mark, and the collection is the value of the property.
        if (propertyElement.Parent is not { } holder || !canCarryMark(holder))
        {
            return false;
        }

        owner = holder;
        property = propertyElement.Name;
        return true;
    }

    private static bool IsPropertyElement(XElement element) => element.Name.LocalName.IndexOf('.') >= 0;

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
