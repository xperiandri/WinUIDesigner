// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using WinUIDesigner.Markup;

namespace WinUIDesigner.Surface.Markup;

/// <summary>
/// Leaves out the one resource of a merged dictionary that the XAML reader rejects, so that the rest of the
/// dictionary, and the document that merges it, still load.
/// </summary>
/// <remarks>
/// <para>
/// The reader rejects a whole text for one resource that uses a key nothing defines, for instance a style that is
/// based on a style of a dictionary the surface could not load. Without this, such a resource empties every
/// dictionary that merges it, and a page then fails on keys that the dictionaries did define. It is a safety net,
/// for the cases that a source Visual Studio does not resolve and a project the runtime cannot load leave behind;
/// whatever the surface can get right (the prepared copy of a library, the staged file of a package) is not left
/// to it.
/// </para>
/// <para>
/// It acts only on a position that the reader reported and that is, in the text the reader was given, the exact start
/// of an attribute, and only on a resource that is in a copy of a prepared dictionary: the content of the document
/// itself is never touched, because Visual Studio counts the children of its own document for live edits. What the
/// reader says about the cause is no evidence (its description is known to belong to another place than its
/// position); the attribute at the position is, and the key it refers to is read from the attribute.
/// </para>
/// <para>
/// The resource is removed from the copies in the text, never from the templates that the cache of prepared
/// dictionaries keeps: whether a key resolves depends on what is merged before the copy, which differs between App.xaml,
/// a page and the construction-action path, so a later document, in which the key may resolve, keeps the resource.
/// It is removed from every copy of the same prepared dictionary in the text at once, because each of them is
/// rejected the same way and a text that holds five copies would otherwise be read six times for one resource.
/// </para>
/// </remarks>
/// <param name="origins">Which prepared dictionary each copy in the text comes from.</param>
/// <param name="isDictionary">Tells whether an element name names a resource dictionary.</param>
/// <param name="limit">The number of resources that may be left out in all.</param>
internal sealed class ResourceEntryQuarantine(ResourceCopyOrigins origins, Func<XName, bool> isDictionary, int limit = ResourceEntryQuarantine.DefaultLimit)
{
    /// <summary>
    /// The number of resources left out by default. Every one costs another read of the whole text.
    /// </summary>
    public const int DefaultLimit = 8;

    private static readonly XName _Key = XName.Get("Key", XamlNamespaces.XamlLanguage);
    private static readonly XName _TargetType = XName.Get("TargetType");

    private static readonly Regex _Position = new(@"\[Line:\s*(?<line>\d+)\s+Position:\s*(?<position>\d+)\]", RegexOptions.CultureInvariant);

    private static readonly Regex _ResourceReference = new(
        @"\{\s*(?:StaticResource|ThemeResource)\s+(?:ResourceKey\s*=\s*)?(?<key>[^\s,}]+)\s*\}",
        RegexOptions.CultureInvariant
    );

    /// <summary>
    /// Gets the number of resources left out so far.
    /// </summary>
    public int Count { get; private set; }

    /// <summary>
    /// Reads the position the reader reports: the last <c>[Line: L Position: P]</c> of its message.
    /// </summary>
    public static bool TryParsePosition(string? message, out int line, out int position)
    {
        line = 0;
        position = 0;
        if (string.IsNullOrEmpty(message))
        {
            return false;
        }

        // A message can quote the text of an earlier error, which has a position of its own; the last one is the error.
        Match? last = null;
        foreach (Match match in _Position.Matches(message))
        {
            last = match;
        }

        return last is not null
            && int.TryParse(last.Groups["line"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out line)
            && int.TryParse(last.Groups["position"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out position)
            && line > 0
            && position > 0;
    }

    /// <summary>
    /// Reads the key a value refers to through <c>{StaticResource Key}</c> or <c>{ThemeResource Key}</c>: the first
    /// reference in the value.
    /// </summary>
    public static bool TryGetReferencedKey(string? value, out string key)
    {
        Match match = string.IsNullOrEmpty(value) ? Match.Empty : _ResourceReference.Match(value);
        key = match.Success ? match.Groups["key"].Value : string.Empty;
        return match.Success;
    }

    /// <summary>
    /// Leaves out the resource that the reader rejected, when it can be found with certainty.
    /// </summary>
    /// <param name="root">The tree that was serialized, changed in place.</param>
    /// <param name="serialized">The exact text that the reader was given.</param>
    /// <param name="readerMessage">The message of the exception the reader threw.</param>
    /// <returns>
    /// What was left out, or <see langword="null"/> when nothing was: no position in the message, a position that
    /// starts no attribute, a resource that is not in a prepared copy, or the limit.
    /// </returns>
    public QuarantinedResource? TryQuarantine(XElement root, string serialized, string readerMessage)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(serialized);

        // The limit.
        if (Count >= limit || !TryParsePosition(readerMessage, out int line, out int position))
        {
            return null;
        }

        // The position has to be the start of an attribute in the text that the reader read.
        if (!TryLocate(root, serialized, line, position, out XElement? element, out string? attributeName, out string? attributeValue))
        {
            return null;
        }

        // The resource has to be in a copy of a prepared dictionary and not in the document itself.
        if (!origins.TryFindCopy(element, out XElement? copy, out ResourceCopyOrigin? origin) || FindEntry(element, copy) is not { } entry)
        {
            return null;
        }

        // The same resource in every copy of that dictionary in the text, found by its place in the copy.
        int[] path = GetPath(entry, copy);
        string? key = GetKey(entry);
        var doomed = new List<XElement> { entry };
        foreach (KeyValuePair<XElement, ResourceCopyOrigin> other in origins.FindCopies(root))
        {
            if (!ReferenceEquals(other.Key, copy)
                && !other.Value.IsRuntime
                && string.Equals(other.Value.Path, origin.Path, StringComparison.OrdinalIgnoreCase)
                && Follow(other.Key, path) is { } sibling
                && sibling.Name == entry.Name
                && string.Equals(GetKey(sibling), key, StringComparison.Ordinal))
            {
                doomed.Add(sibling);
            }
        }

        // The dictionaries merged by URI before the resource: the likely source of a key that is missing.
        string[] mergedByUri = origins.FindCopies(root)
            .Where(other => other.Value.IsRuntime && XNode.DocumentOrderComparer.Compare(other.Key, entry) < 0)
            .Select(other => other.Value.Uri)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        foreach (XElement removed in doomed)
        {
            removed.Remove();
        }

        Count++;
        TryGetReferencedKey(attributeValue, out string neededKey);
        return new QuarantinedResource(
            origin.Name,
            origin.Uri,
            entry.Name.LocalName,
            key,
            attributeName,
            attributeValue,
            neededKey.Length == 0 ? null : neededKey,
            line,
            position,
            doomed.Count,
            mergedByUri,
            readerMessage
        );
    }

    // Finds the element of the tree that holds the attribute which starts at the position, as the XML reader counts
    // positions and as the XAML reader reports them: the text is read again with line information, and the element
    // that holds the attribute is matched with the element of the tree that is in the same place of the document.
    private static bool TryLocate(
        XElement root,
        string serialized,
        int line,
        int position,
        [NotNullWhen(true)] out XElement? element,
        [NotNullWhen(true)] out string? attributeName,
        [NotNullWhen(true)] out string? attributeValue)
    {
        element = null;
        attributeName = null;
        attributeValue = null;

        XElement? parsedRoot;
        try
        {
            parsedRoot = XDocument.Parse(serialized, LoadOptions.SetLineInfo).Root;
        }
        catch (XmlException)
        {
            return false;
        }

        if (parsedRoot is null)
        {
            return false;
        }

        // Both trees are walked in step, and the tree has to be the markup the text was made of.
        using IEnumerator<XElement> tree = root.DescendantsAndSelf().GetEnumerator();
        foreach (XElement parsed in parsedRoot.DescendantsAndSelf())
        {
            if (!tree.MoveNext() || parsed.Name != tree.Current.Name)
            {
                return false;
            }

            foreach (XAttribute attribute in parsed.Attributes())
            {
                if (!attribute.IsNamespaceDeclaration && attribute is IXmlLineInfo info && info.LineNumber == line && info.LinePosition == position)
                {
                    element = tree.Current;
                    attributeName = attribute.Name.LocalName;
                    attributeValue = attribute.Value;
                    return true;
                }
            }
        }

        return false;
    }

    // The resource that holds the element: the highest element below the copy that is a child of a dictionary.
    private XElement? FindEntry(XElement element, XElement copy)
    {
        XElement? entry = null;
        for (XElement? current = element; current is not null && !ReferenceEquals(current, copy); current = current.Parent)
        {
            // A property element, such as MergedDictionaries, holds resources and is none; a resource inside a
            // template of another resource belongs to that resource, whose parent is higher up.
            if (!current.Name.LocalName.Contains('.', StringComparison.Ordinal) && current.Parent is { } parent && IsDictionaryElement(parent.Name))
            {
                entry = current;
            }
        }

        return entry;
    }

    private bool IsDictionaryElement(XName name) => name.LocalName == "ResourceDictionary" || isDictionary(name);

    private static string? GetKey(XElement entry)
    {
        // A style without a key is the style of its target type.
        return (string?)entry.Attribute(_Key) ?? (entry.Attribute(_TargetType) is { } target ? $"TargetType={target.Value}" : null);
    }

    private static int[] GetPath(XElement entry, XElement copy)
    {
        var path = new List<int>();
        for (XElement current = entry; !ReferenceEquals(current, copy); current = current.Parent!)
        {
            path.Add(current.NodesBeforeSelf().OfType<XElement>().Count());
        }

        path.Reverse();
        return [.. path];
    }

    private static XElement? Follow(XElement start, int[] path)
    {
        XElement? current = start;
        foreach (int index in path)
        {
            current = current.Elements().ElementAtOrDefault(index);
            if (current is null)
            {
                return null;
            }
        }

        return current;
    }
}

/// <summary>
/// A resource that was left out of the text because the XAML reader rejects it.
/// </summary>
/// <param name="DictionaryName">The name of the prepared dictionary the resource is in.</param>
/// <param name="DictionaryUri">The URI of that dictionary.</param>
/// <param name="ElementName">The element that is the resource, such as <c>Style</c>.</param>
/// <param name="Key">The key of the resource, <see langword="null"/> when it has none.</param>
/// <param name="Attribute">The attribute at the position the reader reported.</param>
/// <param name="AttributeValue">The value of that attribute.</param>
/// <param name="NeededKey">The key that the attribute refers to, when it refers to a resource.</param>
/// <param name="Line">The line the reader reported.</param>
/// <param name="Position">The position in that line the reader reported.</param>
/// <param name="Copies">In how many copies of the dictionary in the text the resource was left out.</param>
/// <param name="MergedByUri">The dictionaries merged with their source before the resource.</param>
/// <param name="ReaderMessage">What the reader said, as it said it.</param>
internal sealed record QuarantinedResource(
    string DictionaryName,
    string DictionaryUri,
    string ElementName,
    string? Key,
    string? Attribute,
    string? AttributeValue,
    string? NeededKey,
    int Line,
    int Position,
    int Copies,
    IReadOnlyList<string> MergedByUri,
    string ReaderMessage)
{
    private const int _MaxValueLength = 120;

    /// <summary>
    /// Returns the warning for the log, for the document or dictionary <paramref name="owner"/> that merges the
    /// dictionary the resource is in.
    /// </summary>
    public string Describe(string owner)
    {
        var text = new StringBuilder();
        text.Append($"Resource '{Key ?? ElementName}' of '{DictionaryName}' merged by {owner} is left out");
        if (Copies > 1)
        {
            text.Append($" of {Copies} copies of that dictionary");
        }

        text.Append($": the XAML reader rejects it at line {Line}, position {Position}");
        if (Attribute is not null)
        {
            string value = AttributeValue is { Length: > _MaxValueLength } ? $"{AttributeValue[.._MaxValueLength]}…" : AttributeValue ?? string.Empty;
            text.Append($" ({Attribute}=\"{value}\")");
        }

        // What the reader says about the cause is quoted, not trusted: its description is known to belong elsewhere.
        if (NeededKey is not null)
        {
            text.Append($"; it needs the resource '{NeededKey}'");
            text.Append(MergedByUri.Count == 0
                ? ", and no dictionary is merged by URI before it."
                : $", and the dictionaries merged by URI before it are {string.Join(", ", MergedByUri)}.");
        }
        else
        {
            text.Append('.');
        }

        text.Append($" The reader reports: {ReaderMessage}");
        return text.ToString();
    }
}
