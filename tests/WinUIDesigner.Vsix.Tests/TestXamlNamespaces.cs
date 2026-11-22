// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using WinUIDesigner.Markup;

/// <summary>
/// The XAML namespaces that only the tests use; the well-known ones are in <see cref="XamlNamespaces"/>.
/// </summary>
internal static class TestXamlNamespaces
{
    /// <summary>
    /// The Uno Platform namespace of the <c>android</c> prefix.
    /// </summary>
    public const string Android = "http://uno.ui/android";

    /// <summary>
    /// The condition of <see cref="Conditional"/>: the text after the <c>?</c>.
    /// </summary>
    public const string Condition = "IsApiContractPresent(Windows.Foundation.UniversalApiContract,7)";

    /// <summary>
    /// The presentation namespace under a condition, as a <c>c7</c> prefix declares it.
    /// </summary>
    public const string Conditional = $"{XamlNamespaces.Presentation}?{Condition}";
}
