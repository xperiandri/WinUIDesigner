// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;

namespace WinUIDesigner.Markup;

/// <summary>
/// Names the stand-in that construction actions create for an element a namespace policy drops from a collection.
/// </summary>
/// <remarks>
/// Visual Studio's document model keeps a dropped element, and it addresses the children of a collection by their
/// index in that model; the live collection has no object for the element. The instance builder for a dropped
/// element in a collection therefore creates this stand-in instead of nothing. Visual Studio then adds the stand-in
/// to the collection, removes it and looks it up like any other child, and the surface records the position as a
/// hole instead of putting an object there. Only the surface knows the type; Visual Studio passes the name on.
/// </remarks>
internal static class DroppedElementStandIn
{
    /// <summary>
    /// The type name a construction action carries for the stand-in.
    /// </summary>
    public const string TypeName = "XSurfUwp.DroppedElement";

    /// <summary>
    /// Returns whether <paramref name="serializedTypeName"/> names the stand-in, with or without an assembly name.
    /// </summary>
    public static bool IsTypeName(string? serializedTypeName)
    {
        if (serializedTypeName is null)
        {
            return false;
        }

        int separator = serializedTypeName.IndexOf(',');
        string typeName = (separator < 0 ? serializedTypeName : serializedTypeName.Substring(0, separator)).Trim();
        return string.Equals(typeName, TypeName, StringComparison.Ordinal);
    }
}
