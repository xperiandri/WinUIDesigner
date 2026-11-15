// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

#pragma warning disable IDE0130 // Namespace does not match folder structure
// The construction actions name this type "XSurfUwp.DroppedElement", next to the other stand-ins Visual Studio knows.
namespace XSurfUwp;
#pragma warning restore IDE0130 // Namespace does not match folder structure

/// <summary>
/// Stands in, within one batch of construction actions, for an element that a namespace policy drops from a collection.
/// </summary>
/// <remarks>
/// Visual Studio counts the element when it addresses the children of the collection, so its instance builder creates
/// this object, and Visual Studio adds it to the collection like any other child. The surface never puts it into a
/// live collection: it records the position as a hole of the collection instead
/// (see <see cref="WinUIDesigner.Markup.DroppedElementStandIn"/>). The object belongs to no document tree and has no
/// members, so nothing is rendered or reported for it.
/// </remarks>
internal sealed class DroppedElement
{
    public override string ToString()
    {
        return "Dropped element";
    }
}
