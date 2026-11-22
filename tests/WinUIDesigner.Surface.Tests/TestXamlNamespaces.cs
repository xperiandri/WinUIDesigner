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
    /// The Uno Platform namespace of the <c>ios</c> prefix.
    /// </summary>
    public const string Ios = "http://uno.ui/ios";

    /// <summary>
    /// The Uno Platform namespace of the <c>not_win</c> prefix, which excludes its members on Windows.
    /// </summary>
    public const string NotWin = "http://uno.ui/not_win";

    /// <summary>
    /// The presentation namespace under a condition, as a <c>c7</c> prefix declares it.
    /// </summary>
    public const string Conditional = $"{XamlNamespaces.Presentation}?IsApiContractPresent(Windows.Foundation.UniversalApiContract,7)";
}
