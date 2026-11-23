// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Xml.Linq;

namespace WinUIDesigner.Surface.Markup;

/// <summary>
/// An element that <see cref="XamlNamespaceRewriter"/> removed from a collection, at the position Visual Studio's
/// document model gives it.
/// </summary>
/// <remarks>
/// Visual Studio keeps the element in its document model and counts it when it addresses the children of the
/// collection by index. The live collection that the XAML reader creates from the rewritten document does not have it.
/// The position lets the surface translate the indices of later edits.
/// </remarks>
internal sealed class XamlDroppedChild
{
    public XamlDroppedChild(XElement owner, XName? property, int index, XElement element)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(element);
        ArgumentOutOfRangeException.ThrowIfNegative(index);

        Owner = owner;
        Property = property;
        Index = index;
        Element = element;
    }

    /// <summary>
    /// Gets the object element whose collection the element belonged to.
    /// </summary>
    public XElement Owner { get; }

    /// <summary>
    /// Gets the name of the property element the element was written in, such as <c>Grid.ColumnDefinitions</c>,
    /// or <see langword="null"/> when it was content of <see cref="Owner"/>.
    /// </summary>
    public XName? Property { get; }

    /// <summary>
    /// Gets the position of the element among the items of the collection, counted as Visual Studio's document model
    /// counts them: the dropped elements that precede it are included.
    /// </summary>
    public int Index { get; }

    /// <summary>
    /// Gets the removed element. It no longer belongs to the document but keeps its line information.
    /// </summary>
    public XElement Element { get; }
}
