// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;

namespace WinUIDesigner.Surface.Common;

/// <summary>
/// Maps the protocol type names of the resource references that Visual Studio's instance builders send to the classes
/// that stand for them in the surface. No assembly defines these names.
/// </summary>
internal static class ResourceReferenceProtocol
{
    /// <summary>
    /// Returns <see cref="StaticResourceReference"/> or <see cref="ThemeResourceReference"/> for their protocol names,
    /// with or without an assembly part, and <see langword="null"/> for any other name.
    /// </summary>
    public static Type? GetReferenceType(string? serializedTypeName)
    {
        if (serializedTypeName is null)
        {
            return null;
        }

        ReadOnlySpan<char> name = serializedTypeName.AsSpan();
        int separator = name.IndexOf(',');
        name = (separator < 0 ? name : name[..separator]).Trim();

        return name switch
        {
            StaticResourceReference.ProtocolTypeName => typeof(StaticResourceReference),
            ThemeResourceReference.ProtocolTypeName => typeof(ThemeResourceReference),
            _ => null,
        };
    }
}
