// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;

namespace WinUIDesigner.Markup;

/// <summary>
/// One <c>xmlns</c> declaration found by <see cref="XamlNamespaceScanner"/>.
/// </summary>
internal readonly struct XamlNamespaceDeclaration
{
    public XamlNamespaceDeclaration(string prefix, XamlNamespaceUri uri, int depth, int lineNumber, int linePosition)
    {
        Prefix = prefix;
        Uri = uri;
        Depth = depth;
        LineNumber = lineNumber;
        LinePosition = linePosition;
    }

    /// <summary>
    /// Gets the declared prefix; empty for the default namespace.
    /// </summary>
    public string Prefix { get; }

    /// <summary>
    /// Gets the parsed namespace the prefix is bound to.
    /// </summary>
    public XamlNamespaceUri Uri { get; }

    /// <summary>
    /// Gets the nesting depth of the declaring element; the root element has depth 0.
    /// </summary>
    public int Depth { get; }

    /// <summary>
    /// Gets the 1-based line of the declaration, or 0 when unknown.
    /// </summary>
    public int LineNumber { get; }

    /// <summary>
    /// Gets the 1-based column of the declaration, or 0 when unknown.
    /// </summary>
    public int LinePosition { get; }

    /// <summary>
    /// Gets a value indicating whether the declaration sits on the root element.
    /// </summary>
    public bool IsOnRoot => Depth == 0;
}

/// <summary>
/// The namespace facts of one XAML text, as collected by <see cref="XamlNamespaceScanner.Scan"/>.
/// </summary>
internal sealed class XamlNamespaceScanResult
{
    private readonly HashSet<string> _ignorablePrefixes;
    private readonly HashSet<(string Prefix, string Namespace)> _usedPlatformNamespaces;

    internal XamlNamespaceScanResult(
        IReadOnlyList<XamlNamespaceDeclaration> declarations,
        HashSet<string> ignorablePrefixes,
        bool hasConditionalNamespaces,
        bool hasPlatformNamespaces,
        bool hasPlatformPrefixedObjectElements,
        HashSet<(string Prefix, string Namespace)> usedPlatformNamespaces,
        bool isComplete)
    {
        Declarations = declarations;
        _ignorablePrefixes = ignorablePrefixes;
        HasConditionalNamespaces = hasConditionalNamespaces;
        HasPlatformNamespaces = hasPlatformNamespaces;
        HasPlatformPrefixedObjectElements = hasPlatformPrefixedObjectElements;
        _usedPlatformNamespaces = usedPlatformNamespaces;
        IsComplete = isComplete;
    }

    /// <summary>
    /// Gets the result for a text without any namespace information.
    /// </summary>
    public static XamlNamespaceScanResult Empty { get; } = new(
        Array.Empty<XamlNamespaceDeclaration>(),
        new HashSet<string>(StringComparer.Ordinal),
        hasConditionalNamespaces: false,
        hasPlatformNamespaces: false,
        hasPlatformPrefixedObjectElements: false,
        usedPlatformNamespaces: new HashSet<(string Prefix, string Namespace)>(),
        isComplete: false
    );

    /// <summary>
    /// Gets every <c>xmlns</c> declaration in document order, including those on nested elements.
    /// </summary>
    public IReadOnlyList<XamlNamespaceDeclaration> Declarations { get; }

    /// <summary>
    /// Gets the prefixes listed in any <c>mc:Ignorable</c> attribute of the text.
    /// </summary>
    public IReadOnlyCollection<string> IgnorablePrefixes => _ignorablePrefixes;

    /// <summary>
    /// Gets a value indicating whether a declared namespace carries a conditional-XAML predicate.
    /// </summary>
    public bool HasConditionalNamespaces { get; }

    /// <summary>
    /// Gets a value indicating whether a declared namespace is an Uno platform namespace (<c>http://uno.ui/...</c>).
    /// </summary>
    public bool HasPlatformNamespaces { get; }

    /// <summary>
    /// Gets a value indicating whether an object element is written under an Uno platform namespace.
    /// Property elements such as <c>&lt;android:Grid.Row&gt;</c> do not count.
    /// </summary>
    public bool HasPlatformPrefixedObjectElements { get; }

    /// <summary>
    /// Gets a value indicating whether the whole text was read. It is <see langword="false"/> when the scan
    /// stopped at malformed XML; the facts collected up to that point are still reported.
    /// </summary>
    public bool IsComplete { get; }

    /// <summary>
    /// Returns whether <paramref name="prefix"/> is listed in any <c>mc:Ignorable</c> attribute of the text.
    /// </summary>
    public bool IsIgnorable(string prefix) => _ignorablePrefixes.Contains(prefix);

    /// <summary>
    /// Returns whether an element or an attribute of the text is written under <paramref name="prefix"/> while the
    /// prefix is bound to the Uno platform namespace <paramref name="namespaceUri"/>, so that the declaration is
    /// used and not only made. Only platform namespaces are tracked; the answer is <see langword="false"/> for any other.
    /// </summary>
    /// <param name="prefix">The prefix of the declaration.</param>
    /// <param name="namespaceUri">The namespace of the declaration as written, a <c>#using:</c> suffix included.</param>
    public bool IsPlatformNamespaceUsed(string prefix, string namespaceUri) => _usedPlatformNamespaces.Contains((prefix, namespaceUri));

    /// <summary>
    /// Returns the namespace of the first declaration of <paramref name="prefix"/>, or <see langword="null"/>.
    /// </summary>
    public string? FindNamespace(string prefix)
    {
        for (int index = 0; index < Declarations.Count; index++)
        {
            XamlNamespaceDeclaration declaration = Declarations[index];
            if (string.Equals(declaration.Prefix, prefix, StringComparison.Ordinal))
            {
                return declaration.Uri.Original;
            }
        }

        return null;
    }
}
