// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

namespace WinUIDesigner.Markup;

/// <summary>
/// The well-known XML namespaces of XAML documents.
/// </summary>
internal static class XamlNamespaces
{
    /// <summary>
    /// The default WinUI presentation namespace.
    /// </summary>
    public const string Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    /// <summary>
    /// The XAML language namespace, usually bound to <c>x</c>.
    /// </summary>
    public const string XamlLanguage = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>
    /// The markup-compatibility namespace, usually bound to <c>mc</c>; it defines <c>mc:Ignorable</c>.
    /// </summary>
    public const string MarkupCompatibility = "http://schemas.openxmlformats.org/markup-compatibility/2006";

    /// <summary>
    /// The Blend design-time namespace, usually bound to <c>d</c>.
    /// </summary>
    public const string BlendDesign = "http://schemas.microsoft.com/expression/blend/2008";

    /// <summary>
    /// The namespace of the <c>xmlns</c> attributes that declare namespaces.
    /// </summary>
    public const string Xmlns = "http://www.w3.org/2000/xmlns/";

    /// <summary>
    /// The start of the namespaces Uno Platform assigns to prefixes that are excluded on Windows,
    /// such as <c>http://uno.ui/android</c>.
    /// </summary>
    public const string UnoPlatformBase = "http://uno.ui/";
}
