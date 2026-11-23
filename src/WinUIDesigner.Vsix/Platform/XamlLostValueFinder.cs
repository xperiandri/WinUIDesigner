// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;
using WinUIDesigner.Markup;

namespace WinUIDesigner.Platform;

/// <summary>
/// Finds the member whose value the document model lost when the member that it kept is dropped.
/// </summary>
/// <remarks>
/// The document model keeps one value per property, the one of the member it reads last. When that member is
/// dropped, because its prefix is bound to a conditional namespace that does not apply, the value of an earlier
/// member that does apply is gone, and the property stays unset in the preview.
/// </remarks>
internal static class XamlLostValueFinder
{
    /// <summary>
    /// Returns the first member that sets the property of <paramref name="dropped"/> and applies, or
    /// <see langword="null"/> when there is none.
    /// </summary>
    /// <param name="dropped">The member the document model kept, which is dropped.</param>
    /// <param name="members">
    /// The members of the element in the order the document model reads them; see <see cref="XamlSourceMember.Members"/>.
    /// </param>
    /// <param name="getBaseNamespace">
    /// Returns the namespace without its condition that a prefix is bound to where the members are written, or
    /// <see langword="null"/> when the prefix is not declared.
    /// </param>
    /// <param name="classify">Returns the verdict for the members written under a prefix.</param>
    public static XamlAttributeName? Find(
        XamlAttributeName dropped,
        IReadOnlyList<XamlAttributeName> members,
        Func<string, string?> getBaseNamespace,
        Func<string, XamlPrefixDisposition> classify)
    {
        ReadOnlySpan<char> property = GetPropertyName(dropped.LocalName);
        string? droppedNamespace = dropped.Prefix.Length == 0 ? null : getBaseNamespace(dropped.Prefix);
        foreach (XamlAttributeName member in members)
        {
            // The dropped member is the one that was read last: nothing after it is another value for this property.
            if (IsSameMember(member, dropped))
            {
                break;
            }

            // Another member with the same name, such as d:DataContext, sets a property of its own.
            if (!GetPropertyName(member.LocalName).SequenceEqual(property))
            {
                continue;
            }

            // A member without a prefix always applies.
            if (member.Prefix.Length != 0)
            {
                // A prefix that is not declared names no namespace, as xmlns:d does not.
                string? memberNamespace = getBaseNamespace(member.Prefix);
                if (memberNamespace is null)
                {
                    continue;
                }

                // A member of another namespace is not another value for the property.
                if (droppedNamespace is not null && !string.Equals(memberNamespace, droppedNamespace, StringComparison.Ordinal))
                {
                    continue;
                }

                // A member that is dropped as well has no value to lose.
                if (classify(member.Prefix) == XamlPrefixDisposition.Drop)
                {
                    continue;
                }
            }

            return member;
        }

        return null;
    }

    private static bool IsSameMember(XamlAttributeName first, XamlAttributeName second)
    {
        return string.Equals(first.Prefix, second.Prefix, StringComparison.Ordinal)
            && string.Equals(first.LocalName, second.LocalName, StringComparison.Ordinal);
    }

    // The name of a property element is that of its type and the property: Border.Background sets Background.
    private static ReadOnlySpan<char> GetPropertyName(string localName)
    {
        int dot = localName.LastIndexOf('.');
        return localName.AsSpan(dot + 1);
    }
}
