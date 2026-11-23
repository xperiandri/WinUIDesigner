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
    /// The Uno Platform namespace of the <c>skia</c> prefix.
    /// </summary>
    public const string Skia = "http://uno.ui/skia";

    /// <summary>
    /// The API contract that the conditions ask about.
    /// </summary>
    public const string Contract = "Windows.Foundation.UniversalApiContract";

    /// <summary>
    /// The condition of <see cref="Conditional"/>: the text after the <c>?</c>.
    /// </summary>
    public const string Condition = $"IsApiContractPresent({Contract},7)";

    /// <summary>
    /// The presentation namespace under a condition, as a <c>c7</c> prefix declares it.
    /// </summary>
    public const string Conditional = $"{XamlNamespaces.Presentation}?{Condition}";

    /// <summary>
    /// The presentation namespace under the opposite of the condition of <see cref="Conditional"/>.
    /// </summary>
    public const string ConditionalNotPresent = $"{XamlNamespaces.Presentation}?IsApiContractNotPresent({Contract},7)";

    /// <summary>
    /// The presentation namespace under a contract version that no Windows has.
    /// </summary>
    public const string ConditionalFuture = $"{XamlNamespaces.Presentation}?IsApiContractPresent({Contract},99)";

    /// <summary>
    /// The presentation namespace under the opposite of the condition of <see cref="ConditionalFuture"/>.
    /// </summary>
    public const string ConditionalFutureNotPresent = $"{XamlNamespaces.Presentation}?IsApiContractNotPresent({Contract},99)";
}
