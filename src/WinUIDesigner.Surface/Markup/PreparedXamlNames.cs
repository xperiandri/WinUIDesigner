// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Xml.Linq;

namespace WinUIDesigner.Surface.Markup;

/// <summary>
/// Tells what the element names of prepared XAML refer to, without loading the XAML.
/// </summary>
internal static class PreparedXamlNames
{
    /// <summary>
    /// Returns whether an element named <paramref name="name"/> can be the markup an object of
    /// <paramref name="runtimeType"/> was loaded from.
    /// </summary>
    /// <remarks>
    /// The namespace is compared as written, so an element under a <c>using:</c> namespace with a condition
    /// matches no type: the XAML reader decides the condition on its own and may have skipped the element, which
    /// must then not claim the source position of an object loaded from other markup. Members whose condition the
    /// namespace policies decide are renamed to the namespace without the condition before they get here.
    /// </remarks>
    public static bool IsMarkupOf(XName name, Type runtimeType)
    {
        string namespaceName = name.NamespaceName;
        if (namespaceName.StartsWith("using:", StringComparison.Ordinal))
        {
            return string.Equals($"{namespaceName.AsSpan(6)}.{name.LocalName}", runtimeType.FullName, StringComparison.Ordinal);
        }

        if (namespaceName.StartsWith("clr-namespace:", StringComparison.Ordinal))
        {
            // An assembly may follow the CLR namespace; it is not part of the type name.
            ReadOnlySpan<char> clrNamespace = namespaceName.AsSpan(14);
            int assemblySeparator = clrNamespace.IndexOf(';');
            if (assemblySeparator >= 0)
            {
                clrNamespace = clrNamespace[..assemblySeparator];
            }

            return string.Equals($"{clrNamespace}.{name.LocalName}", runtimeType.FullName, StringComparison.Ordinal);
        }

        return name.LocalName == runtimeType.Name && runtimeType.Namespace?.StartsWith("Microsoft.UI.Xaml", StringComparison.Ordinal) == true;
    }
}
