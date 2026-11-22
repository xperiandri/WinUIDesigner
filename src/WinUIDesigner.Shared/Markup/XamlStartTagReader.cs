// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;

namespace WinUIDesigner.Markup;

/// <summary>
/// The qualified name of an attribute, or of an element, as it is written in XAML text.
/// </summary>
internal readonly struct XamlAttributeName
{
    private readonly string? _prefix;
    private readonly string? _localName;

    public XamlAttributeName(string prefix, string localName)
    {
        _prefix = prefix;
        _localName = localName;
    }

    /// <summary>
    /// Gets the prefix; empty for an attribute written without one.
    /// </summary>
    public string Prefix => _prefix ?? string.Empty;

    /// <summary>
    /// Gets the name without the prefix, for example <c>CornerRadius</c> or <c>Grid.Row</c>.
    /// </summary>
    public string LocalName => _localName ?? string.Empty;

    /// <inheritdoc/>
    public override string ToString() => Prefix.Length == 0 ? LocalName : $"{Prefix}:{LocalName}";
}

/// <summary>
/// Reads the attribute names of an element start tag, and the property elements in the content of an element,
/// from their text, without building a document model.
/// </summary>
/// <remarks>
/// The designer's document model keeps one value per property. When a property is written twice under
/// different prefixes, as an if/else pair of conditional attributes or property elements is, only the text
/// still shows both.
/// </remarks>
internal static class XamlStartTagReader
{
    /// <summary>
    /// Returns the names of the attributes in <paramref name="startTagText"/>, in the order they are written.
    /// Never throws: the names read up to malformed text are returned.
    /// </summary>
    /// <param name="startTagText">Text that begins with an element start tag, such as <c>&lt;Border c7:CornerRadius="4" /&gt;</c>.</param>
    public static IReadOnlyList<XamlAttributeName> ReadAttributeNames(string? startTagText)
    {
        var names = new List<XamlAttributeName>();

        // There is no text.
        if (startTagText is null)
        {
            return names;
        }

        // The text does not begin with a start tag.
        int index = SkipWhitespace(startTagText, 0);
        if (index >= startTagText.Length || startTagText[index] != '<')
        {
            return names;
        }

        // The element name runs up to the first white space or the end of the tag.
        index++;
        while (index < startTagText.Length && !char.IsWhiteSpace(startTagText[index]) && startTagText[index] != '>' && startTagText[index] != '/')
        {
            index++;
        }

        while (true)
        {
            // The end of the tag: there are no more attributes.
            index = SkipWhitespace(startTagText, index);
            if (index >= startTagText.Length || startTagText[index] == '>' || startTagText[index] == '/')
            {
                return names;
            }

            int nameStart = index;
            while (index < startTagText.Length && !char.IsWhiteSpace(startTagText[index]) && startTagText[index] != '=' && startTagText[index] != '>' && startTagText[index] != '/')
            {
                index++;
            }

            string qualifiedName = startTagText.Substring(nameStart, index - nameStart);

            // An attribute without a value is no XML.
            index = SkipWhitespace(startTagText, index);
            if (index >= startTagText.Length || startTagText[index] != '=')
            {
                return names;
            }

            // A value is quoted.
            index = SkipWhitespace(startTagText, index + 1);
            if (index >= startTagText.Length || (startTagText[index] != '"' && startTagText[index] != '\''))
            {
                return names;
            }

            // The value may contain '>' and the other kind of quote; it ends at the quote it began with.
            int valueEnd = startTagText.IndexOf(startTagText[index], index + 1);
            if (valueEnd < 0)
            {
                return names;
            }

            index = valueEnd + 1;

            if (!TryParseName(qualifiedName, out XamlAttributeName name))
            {
                // Not a qualified name; XML would reject the tag, so nothing after it can be trusted.
                return names;
            }

            names.Add(name);
        }
    }

    /// <summary>
    /// Reads the name of an attribute from its text, such as <c>c7:CornerRadius="4"</c>.
    /// </summary>
    /// <returns><see langword="false"/> when the text does not begin with an attribute name.</returns>
    public static bool TryReadAttributeName(string? attributeText, out XamlAttributeName name)
    {
        // There is no text.
        name = default;
        if (attributeText is null)
        {
            return false;
        }

        int start = SkipWhitespace(attributeText, 0);
        int end = start;
        while (end < attributeText.Length && !char.IsWhiteSpace(attributeText[end]) && attributeText[end] != '=' && attributeText[end] != '>' && attributeText[end] != '/')
        {
            end++;
        }

        return TryParseName(attributeText.Substring(start, end - start), out name);
    }

    /// <summary>
    /// Reads the name of an element from the text of its start tag, such as <c>&lt;c7:Border.Background&gt;</c>.
    /// </summary>
    /// <returns><see langword="false"/> when the text does not begin with a start tag that has a name.</returns>
    public static bool TryReadElementName(string? startTagText, out XamlAttributeName name)
    {
        // There is no text.
        name = default;
        if (startTagText is null)
        {
            return false;
        }

        // The text does not begin with a start tag.
        int start = SkipWhitespace(startTagText, 0);
        if (start >= startTagText.Length || startTagText[start] != '<')
        {
            return false;
        }

        int end = start + 1;
        while (end < startTagText.Length && !char.IsWhiteSpace(startTagText[end]) && startTagText[end] != '>' && startTagText[end] != '/')
        {
            end++;
        }

        return TryParseName(startTagText.Substring(start + 1, end - start - 1), out name);
    }

    /// <summary>
    /// Returns the names of the property elements, such as <c>&lt;c7:Border.Background&gt;</c>, that are children
    /// of an element, in the order they are written. Elements inside a child, comments, CDATA sections and
    /// processing instructions are skipped. Never throws: the names read up to malformed text are returned.
    /// </summary>
    /// <param name="contentText">
    /// The text of the content of an element, from the end of its start tag, up to the place that is of interest.
    /// </param>
    public static IReadOnlyList<XamlAttributeName> ReadPropertyElementNames(string? contentText)
    {
        var names = new List<XamlAttributeName>();

        // There is no text.
        if (contentText is null)
        {
            return names;
        }

        // How many child elements are open at the current place of the text.
        int depth = 0;
        int index = 0;
        while (index < contentText.Length)
        {
            // No tag is left in the text.
            int open = contentText.IndexOf('<', index);
            if (open < 0)
            {
                break;
            }

            // Comments, CDATA sections and processing instructions are no elements.
            if (string.CompareOrdinal(contentText, open, "<!--", 0, 4) == 0)
            {
                index = SkipPast(contentText, open + 4, "-->");
                continue;
            }

            if (string.CompareOrdinal(contentText, open, "<![CDATA[", 0, 9) == 0)
            {
                index = SkipPast(contentText, open + 9, "]]>");
                continue;
            }

            if (string.CompareOrdinal(contentText, open, "<?", 0, 2) == 0)
            {
                index = SkipPast(contentText, open + 2, "?>");
                continue;
            }

            // An end tag closes a child; one with no child open is the end of the content itself.
            if (open + 1 < contentText.Length && contentText[open + 1] == '/')
            {
                if (depth == 0)
                {
                    break;
                }

                depth--;
                index = SkipPast(contentText, open + 2, ">");
                continue;
            }

            // A start tag: its name runs up to white space or the end of the tag, and the tag up to a '>'
            // that is not inside an attribute value.
            int nameEnd = open + 1;
            while (nameEnd < contentText.Length && !char.IsWhiteSpace(contentText[nameEnd]) && contentText[nameEnd] != '>' && contentText[nameEnd] != '/')
            {
                nameEnd++;
            }

            // The tag is not closed.
            int tagEnd = FindTagEnd(contentText, nameEnd);
            if (tagEnd < 0)
            {
                break;
            }

            // A property element is a child of the element itself whose name has a type and a property.
            if (depth == 0
                && TryParseName(contentText.Substring(open + 1, nameEnd - open - 1), out XamlAttributeName name)
                && name.LocalName.IndexOf('.') > 0)
            {
                names.Add(name);
            }

            // A tag that does not close itself opens a child.
            if (contentText[tagEnd - 1] != '/')
            {
                depth++;
            }

            index = tagEnd + 1;
        }

        return names;
    }

    private static int SkipPast(string text, int index, string terminator)
    {
        int end = text.IndexOf(terminator, index, StringComparison.Ordinal);
        return end < 0 ? text.Length : end + terminator.Length;
    }

    private static int FindTagEnd(string text, int index)
    {
        for (; index < text.Length; index++)
        {
            // The end of the tag.
            char current = text[index];
            if (current == '>')
            {
                return index;
            }

            // The value of an attribute ends at the quote it began with and may contain a '>'.
            if (current == '"' || current == '\'')
            {
                // The quote is not closed.
                int valueEnd = text.IndexOf(current, index + 1);
                if (valueEnd < 0)
                {
                    return -1;
                }

                index = valueEnd;
            }
        }

        return -1;
    }

    private static bool TryParseName(string qualifiedName, out XamlAttributeName name)
    {
        // A name, or a prefix and a name, each beginning with a letter or an underscore; one colon at most.
        int colon = qualifiedName.IndexOf(':');
        if (qualifiedName.Length == 0
            || colon == 0
            || colon == qualifiedName.Length - 1
            || colon != qualifiedName.LastIndexOf(':')
            || !IsNameStart(qualifiedName[0])
            || (colon > 0 && !IsNameStart(qualifiedName[colon + 1])))
        {
            name = default;
            return false;
        }

        name = colon < 0
            ? new XamlAttributeName(string.Empty, qualifiedName)
            : new XamlAttributeName(qualifiedName.Substring(0, colon), qualifiedName.Substring(colon + 1));
        return true;
    }

    private static bool IsNameStart(char character) => char.IsLetter(character) || character == '_';

    private static int SkipWhitespace(string text, int index)
    {
        while (index < text.Length && char.IsWhiteSpace(text[index]))
        {
            index++;
        }

        return index;
    }
}
