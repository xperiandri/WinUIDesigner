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
    /// <c>DependencyObject</c>. <see langword="null"/> when every element can. It is asked once per element, and an
    /// element it throws for counts as one that can: the load without marks is the safety net for it.
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
        Func<XElement, bool>? canCarry = canCarryMark is null ? null : AskOncePerElement(canCarryMark);
        foreach (XamlDroppedChild dropped in droppedChildren)
        {
            // No element can carry the mark for this collection, or the reader does not create the element.
            if (!TryGetCarrier(dropped, canCarry, out XElement owner, out XName? property) || !IsLoadedWith(owner, runtimeRoot))
            {
                continue;
            }

            // The first collection of an owner marks it; its other collections share the mark.
            if (!marks.TryGetValue(owner, out string? mark))
            {
                mark = marks.Count.ToString(CultureInfo.InvariantCulture);
                marks.Add(owner, mark);
                owner.SetAttributeValue(MarkName, mark);
            }

            // The first dropped element of a collection starts its entry.
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
    /// Loads a document whose owners are marked; when that fails, loads it again without the marks.
    /// </summary>
    /// <typeparam name="T">What loading the document gives.</typeparam>
    /// <param name="collections">What <see cref="Mark"/> returned. A document without marks is loaded once.</param>
    /// <param name="load">Loads the document from its current text.</param>
    /// <param name="marksFailure">
    /// The failure of the load with marks when the load without them worked, which is the case in which the marks were
    /// the reason; <see langword="null"/> otherwise.
    /// </param>
    /// <returns>The loaded document.</returns>
    /// <remarks>
    /// The first failure says nothing about its cause: a missing resource or a constructor that throws fails the load
    /// as well. Only a load that works without the marks shows that they were the reason. When it fails again, the
    /// document fails for another reason, and its exception is the one that is thrown.
    /// </remarks>
    public static T Load<T>(IReadOnlyCollection<DroppedChildCollection> collections, Func<T> load, out Exception? marksFailure)
    {
        ArgumentNullException.ThrowIfNull(collections);
        ArgumentNullException.ThrowIfNull(load);

        marksFailure = null;

        // Without marks there is nothing to blame, and nothing to retry.
        if (collections.Count == 0)
        {
            return load();
        }

        try
        {
            return load();
        }
        catch (Exception ex)
        {
            // The marks may be the reason: load the document as it was prepared.
            Unmark(collections);
            T document = load();
            marksFailure = ex;
            return document;
        }
    }

    /// <summary>
    /// Removes the marks <see cref="Mark"/> added, so that the document can be loaded as it was prepared.
    /// </summary>
    /// <remarks>
    /// The XAML reader can only set the mark on a <c>DependencyObject</c>; on any other object it fails the whole
    /// document. <see cref="Mark"/> leaves out the elements it knows are not a <c>DependencyObject</c>; one whose type
    /// it could not tell is still marked, and loading the document again without marks gives it as it was before marks
    /// existed.
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

        // The owner carries its own mark.
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

    /// <summary>
    /// Wraps <paramref name="canCarryMark"/> so that it is asked once per element, and so that an element it throws
    /// for counts as one that can carry the mark.
    /// </summary>
    /// <remarks>
    /// Every dropped element of a collection asks about the same owner, and for an element of a project type the
    /// answer takes a look-up of the type. A look-up that fails, such as one for a type whose assembly cannot be
    /// loaded, is no reason to fail the document: the element is marked as it is when nothing is known about it, and
    /// <see cref="Load{T}"/> is the safety net.
    /// </remarks>
    private static Func<XElement, bool> AskOncePerElement(Func<XElement, bool> canCarryMark)
    {
        var answers = new Dictionary<XElement, bool>();
        return element =>
        {
            // An earlier dropped element of the same owner asked already.
            if (answers.TryGetValue(element, out bool answer))
            {
                return answer;
            }

            try
            {
                answer = canCarryMark(element);
            }
            catch (Exception ex)
            {
                // Nothing is known about the element, so it is marked.
                WinUIDesignerLogger.LogTrace("Surface", $"Child indices: whether <{element.Name.LocalName}> can carry the mark could not be told, so it is marked: {ex.Message}");
                answer = true;
            }

            answers.Add(element, answer);
            return answer;
        };
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
            // Template content is not created while the document loads.
            if (current.Name.LocalName is "ControlTemplate" or "DataTemplate" or "ItemsPanelTemplate")
            {
                return false;
            }

            // The loaded element is reached: the owner is part of it.
            if (ReferenceEquals(current, runtimeRoot))
            {
                return true;
            }
        }

        // The owner is outside the loaded element.
        return false;
    }
}
