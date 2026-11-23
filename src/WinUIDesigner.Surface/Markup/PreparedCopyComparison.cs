// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using WinUIDesigner.Markup;

namespace WinUIDesigner.Surface.Markup;

/// <summary>
/// Compares the copy of a dictionary that Visual Studio prepared with the copy of the same file that the build laid
/// out, to find the resources that Visual Studio left out.
/// </summary>
/// <remarks>
/// <para>
/// Visual Studio removes a style whose <c>TargetType</c> it cannot resolve when it prepares a copy. It prepares a
/// copy once, while the document is registered, and a copy that it prepares before the types of the project are
/// known (right after it starts, while the project loads) lacks every style of a type of the project, and stays
/// that way until the designer is reloaded. Nothing in the log says so: a page that uses such a style fails on a
/// missing key, or shows the default look, and the dictionary looks fine.
/// </para>
/// <para>
/// The file that the build laid out in the shadow cache is the source of the dictionary as it was when the project
/// was last built, so every keyed resource in it is one that the prepared copy should have. The comparison counts
/// keyed resources by element and key (an implicit style by its target type) rather than matching them one to one,
/// because a theme dictionary repeats a key in each of its dictionaries. It looks at what the files say, not at what
/// Visual Studio did: a resource that was deleted after the last build is reported as well, and the message says so.
/// </para>
/// </remarks>
internal static class PreparedCopyComparison
{
    /// <summary>
    /// The number of resources a message names.
    /// </summary>
    public const int MaxNamed = 8;

    private static readonly XName _Key = XName.Get("Key", XamlNamespaces.XamlLanguage);
    private static readonly XName _TargetType = XName.Get("TargetType");
    private static readonly XName _Ignorable = XName.Get("Ignorable", XamlNamespaces.MarkupCompatibility);

    /// <summary>
    /// Lists the keyed resources of <paramref name="built"/> that <paramref name="prepared"/> lacks.
    /// </summary>
    /// <param name="prepared">The root of Visual Studio's prepared copy, as it is written.</param>
    /// <param name="built">The root of the copy of the same file that the build laid out.</param>
    /// <param name="resourcesInBuilt">The number of keyed resources of the built copy.</param>
    public static IReadOnlyList<LeftOutResource> FindLeftOut(XElement prepared, XElement built, out int resourcesInBuilt)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        ArgumentNullException.ThrowIfNull(built);

        // How many of each there are in the prepared copy; every one that the built copy has more of is missing.
        var available = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (LeftOutResource resource in ListKeyed(prepared))
        {
            available[resource.Identity] = available.TryGetValue(resource.Identity, out int count) ? count + 1 : 1;
        }

        var leftOut = new List<LeftOutResource>();
        resourcesInBuilt = 0;
        foreach (LeftOutResource resource in ListKeyed(built))
        {
            resourcesInBuilt++;
            if (available.TryGetValue(resource.Identity, out int count) && count > 0)
            {
                available[resource.Identity] = count - 1;
            }
            else
            {
                leftOut.Add(resource);
            }
        }

        return leftOut;
    }

    /// <summary>
    /// Describes what Visual Studio left out, for the log.
    /// </summary>
    /// <param name="dictionaryName">The name that messages use for the dictionary.</param>
    /// <param name="leftOut">The resources that the prepared copy lacks.</param>
    /// <param name="resourcesInBuilt">The number of keyed resources of the built copy.</param>
    public static string Describe(string dictionaryName, IReadOnlyList<LeftOutResource> leftOut, int resourcesInBuilt)
    {
        var text = new StringBuilder();
        text.Append($"Visual Studio's prepared copy of '{dictionaryName}' lacks {leftOut.Count} of the {resourcesInBuilt} resources that the built copy of the file has: ");
        text.Append(string.Join(", ", leftOut.Take(MaxNamed).Select(resource => resource.ToString())));
        if (leftOut.Count > MaxNamed)
        {
            text.Append($", and {leftOut.Count - MaxNamed} more");
        }

        // What is likely, and what to do: the explanation is a guess from what the files say.
        text.Append(". Visual Studio removes a style whose TargetType it cannot resolve when it prepares a copy, which happens ");
        text.Append("while the types of the project are not known to it yet, and it prepares a copy only once; reload the designer to get them back. ");
        text.Append("If the resources were deleted from the file after the last build, ignore this.");
        return text.ToString();
    }

    private static IEnumerable<LeftOutResource> ListKeyed(XElement root)
    {
        // What the reader skips anyway is no resource: the namespaces that mc:Ignorable names, and the design-time one.
        var ignored = new HashSet<string>(StringComparer.Ordinal) { XamlNamespaces.BlendDesign };
        foreach (string prefix in ((string?)root.Attribute(_Ignorable) ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (root.GetNamespaceOfPrefix(prefix) is { } ignorable)
            {
                ignored.Add(ignorable.NamespaceName);
            }
        }

        foreach (XElement element in root.Descendants())
        {
            // A resource is a child of a dictionary; a property element, such as MergedDictionaries, is none.
            if (element.Parent is not { } parent
                || parent.Name.LocalName != "ResourceDictionary"
                || element.Name.LocalName.Contains('.', StringComparison.Ordinal)
                || ignored.Contains(element.Name.NamespaceName))
            {
                continue;
            }

            // Keyed by x:Key, and an implicit style by its target type; anything else cannot be told apart.
            string? key = (string?)element.Attribute(_Key);
            string? targetType = (string?)element.Attribute(_TargetType);
            if (key is null && targetType is null)
            {
                continue;
            }

            yield return new LeftOutResource(element.Name.LocalName, key, targetType);
        }
    }
}

/// <summary>
/// A keyed resource of a dictionary.
/// </summary>
/// <param name="ElementName">The element that is the resource, such as <c>Style</c>.</param>
/// <param name="Key">Its <c>x:Key</c>, <see langword="null"/> for an implicit style.</param>
/// <param name="TargetType">Its <c>TargetType</c>, as written.</param>
internal sealed record LeftOutResource(string ElementName, string? Key, string? TargetType)
{
    /// <summary>
    /// Gets what tells two resources apart in the comparison: the element and the key, or the target type.
    /// </summary>
    public string Identity => $"{ElementName}|{Key ?? $"TargetType={TargetType}"}";

    /// <summary>
    /// Returns how the message names the resource: its key, and the target type of a style.
    /// </summary>
    public override string ToString()
    {
        string name = Key ?? $"TargetType={TargetType}";
        return TargetType is not null && Key is not null ? $"{name} ({ElementName} of {TargetType})" : $"{name} ({ElementName})";
    }
}
