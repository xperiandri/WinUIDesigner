// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;

namespace WinUIDesigner.Surface.Common;

/// <summary>
/// Converts the property names of Visual Studio's XAML actions into the names XAML Diagnostics looks up.
/// </summary>
/// <remarks>
/// An action names a property as <c>Name:DeclaringType</c> with an assembly-qualified declaring type
/// (Visual Studio's
/// <see cref="Microsoft.VisualStudio.DesignTools.RuntimeHost.InstanceBuilders.TypeAndPropertySerializer.FormatFullMemberName(string, string)"/>).
/// XAML Diagnostics resolves a property by its full metadata name, <c>Namespace.Type.Name</c>, and rejects a property
/// whose declaring type does not match the object.
/// </remarks>
internal static class XamlDiagnosticsPropertyName
{
    private const string UwpNamespacePrefix = "Windows.UI.Xaml.";
    private const string WinUINamespacePrefix = "Microsoft.UI.Xaml.";

    /// <param name="actionPropertyName">The property name of the action, with or without its declaring type.</param>
    /// <param name="defaultDeclaringType">The full name of the type to use when the action does not name one.</param>
    public static string FromActionProperty(string actionPropertyName, string defaultDeclaringType)
    {
        ReadOnlySpan<char> actionProperty = actionPropertyName.AsSpan();
        int separator = actionProperty.IndexOf(':');
        ReadOnlySpan<char> name = (separator < 0 ? actionProperty : actionProperty[..separator]).Trim();
        ReadOnlySpan<char> declaringType = separator < 0 ? ReadOnlySpan<char>.Empty : RemoveAssemblyName(actionProperty[(separator + 1)..]);

        // An action that names no declaring type sets a property of the target's own type.
        if (declaringType.IsEmpty)
        {
            declaringType = defaultDeclaringType.AsSpan();
        }

        // Shared Visual Studio contracts can spell framework types with the UWP namespace.
        if (declaringType.StartsWith(UwpNamespacePrefix, StringComparison.Ordinal))
        {
            return $"{WinUINamespacePrefix}{declaringType[UwpNamespacePrefix.Length..]}.{name}";
        }

        return $"{declaringType}.{name}";
    }

    private static ReadOnlySpan<char> RemoveAssemblyName(ReadOnlySpan<char> assemblyQualifiedName)
    {
        // A comma inside brackets separates generic arguments, not the assembly name.
        int depth = 0;
        for (int index = 0; index < assemblyQualifiedName.Length; index++)
        {
            switch (assemblyQualifiedName[index])
            {
                case '[':
                    depth++;
                    break;
                case ']':
                    depth--;
                    break;
                case ',' when depth == 0:
                    return assemblyQualifiedName[..index].Trim();
            }
        }

        return assemblyQualifiedName.Trim();
    }
}
