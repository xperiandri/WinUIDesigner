// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.DesignTools.Markup;
using Microsoft.VisualStudio.DesignTools.Markup.DocumentModel;
using Microsoft.VisualStudio.DesignTools.Markup.Metadata;
using Microsoft.VisualStudio.DesignTools.Markup.Xaml;
using Microsoft.VisualStudio.DesignTools.Utility.Text;
using WinUIDesigner.Markup;

namespace WinUIDesigner.Platform;

/// <summary>
/// The <c>xmlns</c> prefix a document node or one of its properties was written with.
/// </summary>
internal readonly struct XamlSourcePrefix
{
    private readonly string? _prefix;
    private readonly XmlElementReference? _scope;

    public XamlSourcePrefix(string prefix, XmlNamespace? xmlNamespace, bool isIgnorable, XmlElementReference scope)
    {
        _prefix = prefix;
        _scope = scope;
        IsKnown = true;
        Namespace = xmlNamespace;
        IsIgnorable = isIgnorable;

        // The designer splits the condition off the namespace; the policies work with the declared value.
        Uri = xmlNamespace is null
            ? default
            : XamlNamespaceUri.Parse(string.IsNullOrEmpty(xmlNamespace.Condition)
                ? xmlNamespace.Value
                : $"{xmlNamespace.Value}?{xmlNamespace.Condition}");
    }

    /// <summary>
    /// Gets the result for a member whose source text is unavailable: the member has "no prefix".
    /// </summary>
    public static XamlSourcePrefix None => default;

    /// <summary>
    /// Gets a value indicating whether the source text of the member was read.
    /// </summary>
    public bool IsKnown { get; }

    /// <summary>
    /// Gets the prefix; empty for a member written without one.
    /// </summary>
    public string Prefix => _prefix ?? string.Empty;

    /// <summary>
    /// Gets the namespace the prefix is bound to where the member is written, with its
    /// <see cref="XmlNamespace.Condition"/>. It is <see langword="null"/> for an unprefixed attribute
    /// and for a prefix that is not declared.
    /// </summary>
    public XmlNamespace? Namespace { get; }

    /// <summary>
    /// Gets <see cref="Namespace"/> in the form the namespace policies work with.
    /// </summary>
    public XamlNamespaceUri Uri { get; }

    /// <summary>
    /// Gets a value indicating whether the namespace is listed in <c>mc:Ignorable</c> where the member is written.
    /// </summary>
    public bool IsIgnorable { get; }

    /// <summary>
    /// Returns the namespace another prefix is bound to where the member is written, or <see langword="null"/>.
    /// </summary>
    public string? ResolveNamespace(string prefix)
    {
        try
        {
            return _scope?.GetXmlNamespace(XmlnsPrefix.ToPrefix(prefix))?.Value;
        }
        catch (Exception)
        {
            return null;
        }
    }
}

/// <summary>
/// An attribute or a property element as it is written in the XAML source text, together with the members of
/// its element that the document model reads before it.
/// </summary>
internal readonly struct XamlSourceMember
{
    private readonly IReadOnlyList<XamlAttributeName>? _members;
    private readonly XmlElementReference? _owner;

    public XamlSourceMember(XamlAttributeName name, IReadOnlyList<XamlAttributeName> members, XmlElementReference owner, bool isPropertyElement)
    {
        Name = name;
        _members = members;
        _owner = owner;
        IsPropertyElement = isPropertyElement;
        IsKnown = true;
    }

    /// <summary>
    /// Gets a value indicating whether the source text of the member was read.
    /// </summary>
    public bool IsKnown { get; }

    /// <summary>
    /// Gets a value indicating whether the member is a property element, such as <c>&lt;Border.Background&gt;</c>,
    /// and not an attribute.
    /// </summary>
    public bool IsPropertyElement { get; }

    /// <summary>
    /// Gets the name the member is written with.
    /// </summary>
    public XamlAttributeName Name { get; }

    /// <summary>
    /// Gets the names of the members of the element in the order the document model reads them: the attributes in
    /// the order they are written, then the property elements in the order they are written. An attribute is
    /// listed with the attributes that follow it, a property element only with the members before it. The
    /// document model does not keep them all: it keeps one value per property, the one it read last.
    /// </summary>
    public IReadOnlyList<XamlAttributeName> Members => _members ?? Array.Empty<XamlAttributeName>();

    /// <summary>
    /// Returns what <paramref name="prefix"/> is bound to where the member is written.
    /// </summary>
    public XamlSourcePrefix ResolvePrefix(string prefix)
    {
        try
        {
            return _owner is null ? XamlSourcePrefix.None : XamlSourcePrefixReader.Resolve(prefix, _owner);
        }
        catch (Exception)
        {
            return XamlSourcePrefix.None;
        }
    }
}

/// <summary>
/// Reads the prefix a document node was written with from the XAML source text.
/// </summary>
/// <remarks>
/// The document model resolves a prefix to its namespace while parsing and does not keep it, yet
/// <c>win</c>, <c>not_android</c> and the default namespace all resolve to the same namespace.
/// The prefix is therefore read back from the text range the node was parsed from.
/// This is the only place that depends on the source-reference types of the designer.
/// </remarks>
internal static class XamlSourcePrefixReader
{
    // A qualified name is short; the limit bounds what is read for a start tag of any length.
    private const int _MaxNameLength = 512;

    // Bounds what is read to list the attributes of one element.
    private const int _MaxStartTagLength = 64 * 1024;

    // Bounds what is read to list the property elements in front of one.
    private const int _MaxContentLength = 256 * 1024;

    /// <summary>
    /// Returns the prefix the element of <paramref name="node"/> was written with.
    /// </summary>
    /// <returns>The prefix, or <see cref="XamlSourcePrefix.None"/> when it cannot be read for any reason.</returns>
    public static XamlSourcePrefix ReadElementPrefix(DocumentNode? node)
    {
        try
        {
            return node?.SourceContext is XmlElementReference element
                ? ReadElementPrefix(element)
                : XamlSourcePrefix.None;
        }
        catch (Exception)
        {
            return XamlSourcePrefix.None;
        }
    }

    /// <summary>
    /// Returns the prefix <paramref name="property"/> was written with on <paramref name="parentNode"/>,
    /// either as an attribute (<c>win:FontSize="24"</c>) or as a property element (<c>&lt;win:Border.Child&gt;</c>).
    /// </summary>
    /// <returns>The prefix, or <see cref="XamlSourcePrefix.None"/> when it cannot be read for any reason.</returns>
    public static XamlSourcePrefix ReadPropertyPrefix(DocumentCompositeNode? parentNode, IProperty? property)
    {
        try
        {
            // There is no property to read.
            if (parentNode is null || property is null)
            {
                return XamlSourcePrefix.None;
            }

            return ReadContainerPrefix(parentNode.GetContainerContext(property));
        }
        catch (Exception)
        {
            return XamlSourcePrefix.None;
        }
    }

    /// <summary>
    /// Returns the prefix of the attribute or the property element that the source reference
    /// <paramref name="container"/> refers to.
    /// </summary>
    /// <returns>The prefix, or <see cref="XamlSourcePrefix.None"/> for any other container.</returns>
    /// <remarks>Unlike the public methods, this one lets a failure of the designer's references through.</remarks>
    internal static XamlSourcePrefix ReadContainerPrefix(object? container)
    {
        // A property set through element content has no container of its own.
        return container switch
        {
            XmlElementReference propertyElement => ReadElementPrefix(propertyElement),
            XmlAttributeReference attribute => ReadAttributePrefix(attribute),
            _ => XamlSourcePrefix.None,
        };
    }

    /// <summary>
    /// Returns the attribute or the property element <paramref name="property"/> was written as on
    /// <paramref name="parentNode"/>, with the other members of that element that the document model reads before it.
    /// </summary>
    /// <returns>
    /// The member; <see cref="XamlSourceMember.IsKnown"/> is <see langword="false"/> when the property was not
    /// written as an attribute or a property element, or its text cannot be read for any reason.
    /// </returns>
    public static XamlSourceMember ReadMember(DocumentCompositeNode? parentNode, IProperty? property)
    {
        try
        {
            // There is no property to read.
            if (parentNode is null || property is null)
            {
                return default;
            }

            return ReadContainerMember(parentNode.GetContainerContext(property));
        }
        catch (Exception)
        {
            return default;
        }
    }

    /// <summary>
    /// Returns the attribute or the property element that the source reference <paramref name="container"/>
    /// refers to, with the other members of its element that the document model reads before it.
    /// </summary>
    /// <returns>The member, or an unknown one for any other container.</returns>
    /// <remarks>Unlike the public methods, this one lets a failure of the designer's references through.</remarks>
    internal static XamlSourceMember ReadContainerMember(object? container)
    {
        switch (container)
        {
            // The attributes of an element are read before its property elements.
            case XmlAttributeReference attribute when attribute.Parent is XmlElementReference attributeOwner
                && XamlStartTagReader.TryReadAttributeName(ReadText(attribute, attribute.TextRange, _MaxNameLength), out XamlAttributeName attributeName):
                IReadOnlyList<XamlAttributeName> attributes = ReadAttributeNames(attributeOwner);
                return new XamlSourceMember(attributeName, attributes, attributeOwner, isPropertyElement: false);

            case XmlElementReference propertyElement when propertyElement.Parent is XmlElementReference elementOwner
                && XamlStartTagReader.TryReadElementName(ReadText(propertyElement, propertyElement.StartTagRange ?? propertyElement.TextRange, _MaxNameLength), out XamlAttributeName elementName):
                var members = new List<XamlAttributeName>(ReadAttributeNames(elementOwner));
                members.AddRange(XamlStartTagReader.ReadPropertyElementNames(ReadContentBefore(elementOwner, propertyElement)));
                return new XamlSourceMember(elementName, members, elementOwner, isPropertyElement: true);

            // A property set through element content has no container of its own.
            default:
                return default;
        }
    }

    private static IReadOnlyList<XamlAttributeName> ReadAttributeNames(XmlElementReference element)
    {
        return XamlStartTagReader.ReadAttributeNames(ReadText(element, element.StartTagRange ?? element.TextRange, _MaxStartTagLength));
    }

    /// <summary>
    /// Reads the content of <paramref name="owner"/> from the end of its start tag up to the start of
    /// <paramref name="member"/>, one of its children.
    /// </summary>
    private static string? ReadContentBefore(XmlElementReference owner, XmlElementReference member)
    {
        // An element that has children has a start tag of its own.
        ITextRange? startTag = owner.StartTagRange;
        IReadableSelectableTextBuffer? buffer = owner.TextBuffer;
        ITextRange? range = member.TextRange;
        if (startTag is null || buffer is null || range is null)
        {
            return null;
        }

        // Nothing is between them, or more than is worth reading: the members before it are not known.
        int start = startTag.Offset + startTag.Length;
        int length = range.Offset - start;
        return start >= 0 && length > 0 && length <= _MaxContentLength && start + length <= buffer.Length
            ? buffer.GetText(start, length)
            : null;
    }

    private static XamlSourcePrefix ReadElementPrefix(XmlElementReference element)
    {
        // An element with content tracks its start tag separately; a self-closing element is its own start tag.
        string? prefix = XamlNamespaceScanner.ReadElementPrefix(ReadText(element, element.StartTagRange ?? element.TextRange));
        return prefix is null ? XamlSourcePrefix.None : Resolve(prefix, element);
    }

    private static XamlSourcePrefix ReadAttributePrefix(XmlAttributeReference attribute)
    {
        // An attribute outside an element has no scope to resolve its prefix in.
        if (attribute.Parent is not XmlElementReference owner)
        {
            return XamlSourcePrefix.None;
        }

        // The range does not start with an attribute name.
        string? prefix = XamlNamespaceScanner.ReadAttributePrefix(ReadText(attribute, attribute.TextRange));
        if (prefix is null)
        {
            return XamlSourcePrefix.None;
        }

        // The default namespace applies to elements only: an unprefixed attribute is in no namespace.
        return prefix.Length == 0
            ? new XamlSourcePrefix(prefix, xmlNamespace: null, isIgnorable: false, owner)
            : Resolve(prefix, owner);
    }

    internal static XamlSourcePrefix Resolve(string prefix, XmlElementReference scope)
    {
        XmlNamespace? xmlNamespace = scope.GetXmlNamespace(XmlnsPrefix.ToPrefix(prefix));
        bool isIgnorable = xmlNamespace is not null && scope.IsIgnorable(xmlNamespace);
        return new XamlSourcePrefix(prefix, xmlNamespace, isIgnorable, scope);
    }

    private static string? ReadText(XamlSourceContext context, ITextRange? range, int maxLength = _MaxNameLength)
    {
        // A reference without text or without a range has nothing to read.
        IReadableSelectableTextBuffer? buffer = context.TextBuffer;
        if (buffer is null || range is null)
        {
            return null;
        }

        // A range that starts before the text does not describe it.
        int offset = range.Offset;
        if (offset < 0)
        {
            return null;
        }

        // Only the start of the range is read, and a range at or past the end of the text has nothing to read.
        int length = Math.Min(Math.Min(range.Length, maxLength), buffer.Length - offset);
        return length > 0 ? buffer.GetText(offset, length) : null;
    }
}
