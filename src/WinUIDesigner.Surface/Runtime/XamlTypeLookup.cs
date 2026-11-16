// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;

namespace WinUIDesigner.Surface.Runtime;

/// <summary>
/// Finds the XAML metadata of a CLR type among several metadata providers. It has no WinUI types of its own, so that
/// the rule can be tested.
/// </summary>
internal static class XamlTypeLookup
{
    /// <summary>
    /// The name by which the metadata providers that the XAML compiler generates know a type. They write a nested type
    /// with a dot (<c>Uno.Toolkit.UI.SafeArea.InsetMask</c>), but <see cref="Type.FullName"/> separates it from the type
    /// that contains it with a plus sign (<c>Uno.Toolkit.UI.SafeArea+InsetMask</c>). A constructed generic type has no
    /// such name, and the result for it is <see langword="null"/>.
    /// </summary>
    public static string? GetXamlName(Type type)
    {
        return type.IsGenericType || type.FullName is not { } fullName ? null : fullName.Replace('+', '.');
    }

    /// <summary>
    /// Asks each provider about the type itself, which finds every type that the provider describes, and only then
    /// asks by name.
    /// </summary>
    public static T? Find<T>(Type type, IEnumerable<Func<Type, T?>> providers, Func<string, T?> byName) where T : class
    {
        foreach (Func<Type, T?> provider in providers)
        {
            if (provider(type) is { } found)
            {
                return found;
            }
        }

        return GetXamlName(type) is { } name ? byName(name) : null;
    }
}
