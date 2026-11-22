// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;

namespace WinUIDesigner.Markup;

/// <summary>
/// Decides whether the members under an Uno Platform prefix apply to the previewed platform, as in
/// <c>xmlns:win="http://schemas.microsoft.com/winfx/2006/xaml/presentation"</c> or
/// <c>xmlns:android="http://uno.ui/android"</c>.
/// </summary>
/// <remarks>
/// <para>
/// The decision is driven by the prefix: <c>win</c>, <c>not_android</c> and the default namespace
/// all share the presentation URI. The prefixes are those of the "Available prefixes" tables of
/// https://platform.uno/docs/articles/platform-specific-xaml.html, in every version of that page:
/// Uno 7 names target frameworks (<c>desktop</c>, <c>tvos</c>, <c>winappsdk</c>), earlier versions also
/// name the renderer (<c>skia</c>, <c>androidskia</c>) and the reference build of a library (<c>netstdref</c>).
/// </para>
/// <para>
/// The previewed platform is one of the target names of the table below. The classification is the same for every
/// target, but only prepared XAML can show a target other than Windows: the document model that construction actions
/// are generated from has no nodes for the members of an ignorable Uno namespace (<c>android:Text</c>,
/// <c>&lt;not_win:Border&gt;</c>), and the action path can only drop properties. Another target would therefore also
/// have to keep every document on the prepared-XAML route, with the second cleaning that keeps the raw elements.
/// </para>
/// </remarks>
internal static class UnoPlatformPrefixes
{
    /// <summary>
    /// The platform the designer previews unless told otherwise.
    /// </summary>
    public const string DefaultTargetPlatform = _Windows;

    /// <summary>
    /// The environment variable of the Visual Studio extension that turns on preparing a document a second time,
    /// so that its object elements under platform prefixes reach the surface. The warning for a namespace that is
    /// missing from <c>mc:Ignorable</c> names it, because the setting changes what becomes of those elements.
    /// </summary>
    public const string ReprepareRawElementsVariable = "WINUIDESIGNER_REPREPARE_RAW_PLATFORM_ELEMENTS";

    private const string _Windows = "win";
    private const string _NegationPrefix = "not_";
    private const string _ClrNamespaceScheme = "clr-namespace:";

    // The prefix of Uno 7 for the Windows App SDK; "win" is its original spelling.
    private const string _WindowsAppSdk = "winappsdk";

    // The prefix of Uno 4 for every platform but Windows, which is what "not_win" says.
    private const string _Xamarin = "xamarin";

    /// <summary>
    /// Every documented platform prefix with the targets whose builds include its members.
    /// A target that renders with Skia is included by <c>skia</c> as well, and <c>netstdref</c> covers
    /// what Uno builds against its reference assembly: the Skia targets and WebAssembly.
    /// </summary>
    private static readonly KeyValuePair<string, string[]>[] _Platforms =
    [
        Platform(_Windows, _Windows),
        Platform(_WindowsAppSdk, _Windows),
        Platform("android", "android"),
        Platform("ios", "ios"),
        Platform("tvos", "tvos"),
        Platform("macos", "macos"),
        Platform("wasm", "wasm"),
        Platform("desktop", "desktop"),
        Platform("skia", "skia", "desktop", "androidskia", "iosskia", "wasmskia"),
        Platform("androidskia", "androidskia"),
        Platform("iosskia", "iosskia"),
        Platform("wasmskia", "wasmskia"),
        Platform("netstdref", "netstdref", "wasm", "skia", "desktop", "androidskia", "iosskia", "wasmskia"),
    ];

    /// <summary>
    /// Returns whether <paramref name="namespaceUri"/> is an Uno platform namespace such as <c>http://uno.ui/android</c>.
    /// </summary>
    public static bool IsPlatformUri(string? namespaceUri)
    {
        return namespaceUri is not null && namespaceUri.StartsWith(XamlNamespaces.UnoPlatformBase, StringComparison.Ordinal);
    }

    /// <summary>
    /// Splits a documented platform prefix into the platform it names and whether it stands for every
    /// platform but that one, as <c>not_android</c> does.
    /// </summary>
    /// <param name="prefix">The prefix as written. It is compared exactly, as Uno does: <c>Android</c> is not a platform prefix.</param>
    /// <param name="platform">The platform prefix without its negation, for example <c>android</c>.</param>
    /// <param name="isNegated">Whether the members apply everywhere except on <paramref name="platform"/>.</param>
    /// <returns><see langword="false"/> when <paramref name="prefix"/> is not a documented platform prefix.</returns>
    public static bool TryParse(string? prefix, out string platform, out bool isNegated)
    {
        platform = string.Empty;
        isNegated = false;
        if (prefix is null || prefix.Length == 0)
        {
            return false;
        }

        if (string.Equals(prefix, _Xamarin, StringComparison.Ordinal))
        {
            platform = _Windows;
            isNegated = true;
            return true;
        }

        bool hasNegation = prefix.StartsWith(_NegationPrefix, StringComparison.Ordinal);
        string name = hasNegation ? prefix.Substring(_NegationPrefix.Length) : prefix;
        if (FindTargets(name) is null)
        {
            return false;
        }

        platform = name;
        isNegated = hasNegation;
        return true;
    }

    /// <summary>
    /// Returns whether Uno includes the members written under the documented platform prefix
    /// <paramref name="prefix"/> in a build for <paramref name="targetPlatform"/>.
    /// </summary>
    /// <returns><see langword="false"/> also when <paramref name="prefix"/> is not a platform prefix.</returns>
    public static bool IsIncluded(string? prefix, string? targetPlatform)
    {
        return TryParse(prefix, out string platform, out bool isNegated) && IsIncluded(platform, isNegated, targetPlatform);
    }

    /// <summary>
    /// Classifies the members written under <paramref name="prefix"/> for <paramref name="targetPlatform"/>.
    /// </summary>
    /// <param name="prefix">The prefix as written in the document; empty for the default namespace.</param>
    /// <param name="uri">The parsed namespace the prefix is bound to.</param>
    /// <param name="isIgnorable">Whether the prefix is listed in <c>mc:Ignorable</c> where it is used.</param>
    /// <param name="targetPlatform">The previewed platform, for example <see cref="DefaultTargetPlatform"/>.</param>
    /// <returns>
    /// <see cref="XamlPrefixDisposition.Keep"/> for a prefix included on the platform, <see cref="XamlPrefixDisposition.Drop"/>
    /// for an excluded one, and <see cref="XamlPrefixDisposition.Leave"/> for a prefix that is not a platform prefix.
    /// </returns>
    /// <remarks>
    /// A platform prefix decides whether or not it is listed in <c>mc:Ignorable</c>; a prefix that should be
    /// listed and is not is found by <see cref="FindMissingIgnorable"/>, for the caller to report. On Windows a
    /// platform prefix is kept exactly when it is bound to the presentation namespace, whatever its name says.
    /// </remarks>
    public static XamlPrefixDisposition Classify(string prefix, XamlNamespaceUri uri, bool isIgnorable, string targetPlatform)
    {
        // The default namespace has no prefix that could name a platform.
        if (string.IsNullOrEmpty(prefix))
        {
            return XamlPrefixDisposition.Leave;
        }

        if (TryParse(prefix, out string platform, out bool isNegated))
        {
            // A prefix bound to a CLR namespace names the types of that namespace, whatever it is called:
            // xmlns:skia="using:SkiaSharp.Views.Windows" and xmlns:desktop="using:My.App.Desktop" are ordinary namespaces.
            if (IsClrNamespace(uri.BaseUri))
            {
                return XamlPrefixDisposition.Leave;
            }

            // Uno's own compiler includes a prefix whatever namespace it is bound to, as the table says. Windows is
            // built by the Windows XAML compiler, which knows no platform prefixes and includes what it can resolve:
            // of the namespaces a platform prefix is bound to, that is the presentation namespace alone. So on
            // Windows the namespace decides, in both directions: not_win bound to the presentation namespace is
            // kept, and not_netstdref, which the documentation binds to an Uno namespace, is left out.
            bool isIncluded = IsWindows(targetPlatform)
                ? string.Equals(uri.BaseUri, XamlNamespaces.Presentation, StringComparison.Ordinal)
                : IsIncluded(platform, isNegated, targetPlatform);

            return isIncluded ? XamlPrefixDisposition.Keep : XamlPrefixDisposition.Drop;
        }

        // An Uno namespace under a prefix the table does not know. Nothing resolves such a namespace,
        // so when it is ignorable every XAML reader skips its members.
        if (isIgnorable && IsPlatformUri(uri.BaseUri))
        {
            return XamlPrefixDisposition.Drop;
        }

        return XamlPrefixDisposition.Leave;
    }

    /// <summary>
    /// Returns the declarations of <paramref name="scan"/> that bind a prefix to an Uno platform namespace
    /// without listing the prefix in <c>mc:Ignorable</c>, for a prefix that an element or an attribute is written
    /// under. The Windows XAML compiler cannot resolve such a namespace and rejects the document; the namespace
    /// policies classify a platform prefix regardless.
    /// </summary>
    /// <remarks>
    /// A declaration that nothing uses is not reported: the compiler does not look at it, and a document may
    /// declare every platform prefix of Uno as boilerplate.
    /// </remarks>
    public static IReadOnlyList<XamlNamespaceDeclaration> FindMissingIgnorable(XamlNamespaceScanResult scan)
    {
        List<XamlNamespaceDeclaration>? missing = null;
        IReadOnlyList<XamlNamespaceDeclaration> declarations = scan.Declarations;
        for (int index = 0; index < declarations.Count; index++)
        {
            XamlNamespaceDeclaration declaration = declarations[index];
            if (declaration.Prefix.Length != 0
                && IsPlatformUri(declaration.Uri.BaseUri)
                && !scan.IsIgnorable(declaration.Prefix)
                && scan.IsPlatformNamespaceUsed(declaration.Prefix, declaration.Uri.Original))
            {
                (missing ??= []).Add(declaration);
            }
        }

        return missing ?? (IReadOnlyList<XamlNamespaceDeclaration>)Array.Empty<XamlNamespaceDeclaration>();
    }

    /// <summary>
    /// Describes a declaration found by <see cref="FindMissingIgnorable"/> for the log: where it is, what is wrong
    /// with it, and what the designer makes of the members under its prefix.
    /// </summary>
    /// <param name="path">The path of the document, or <see langword="null"/> when it has none.</param>
    /// <param name="declaration">The declaration that is missing from <c>mc:Ignorable</c>.</param>
    /// <param name="reprepareRawElements">
    /// Whether documents are prepared a second time, so that the object elements under platform prefixes reach the
    /// surface (<see cref="ReprepareRawElementsVariable"/>).
    /// </param>
    public static string DescribeMissingIgnorable(string? path, XamlNamespaceDeclaration declaration, bool reprepareRawElements)
    {
        string consequence;
        if (!TryParse(declaration.Prefix, out _, out _))
        {
            // The table does not know the prefix, so nothing classifies it unless it is ignorable.
            consequence = "Its members are left as written for the XAML reader to reject.";
        }
        else if (reprepareRawElements)
        {
            // The prepared XAML keeps the raw elements, and the surface drops every member of the prefix.
            consequence = "The designer applies the platform prefix regardless.";
        }
        else
        {
            // Attributes and property elements are dropped on both paths, but an object element reaches the
            // surface as construction actions, where Visual Studio makes it a node of an unknown type.
            consequence = $"The designer drops its attributes and property elements regardless, but its object elements appear as elements of an unknown type unless {ReprepareRawElementsVariable}=1 is set.";
        }

        return $"{path}({declaration.LineNumber},{declaration.LinePosition}): xmlns:{declaration.Prefix}=\"{declaration.Uri.Original}\" is an Uno platform namespace that the document uses but does not list in mc:Ignorable, " +
            $"so the Windows XAML compiler cannot resolve it. {consequence}";
    }

    /// <summary>
    /// Decides how a document reaches the surface, from what a scan of its text found.
    /// </summary>
    /// <param name="scan">The namespace facts of the document.</param>
    /// <param name="reprepareRawElements">
    /// Whether a document may be prepared a second time to keep its object elements under platform prefixes.
    /// </param>
    /// <param name="tryReprepare">
    /// Prepares the document a second time and returns whether its prepared XAML now has those elements. It is only
    /// called for a document that has them, and only when <paramref name="reprepareRawElements"/> is set.
    /// </param>
    public static PlatformElementsBuild ChooseBuild(XamlNamespaceScanResult scan, bool reprepareRawElements, Func<bool> tryReprepare)
    {
        // Attributes and property elements under a platform prefix pass through the XAML cleaner as they are
        // written. Object elements do not: the cleaner cannot resolve their type and replaces each of them with a
        // fallback control without its children.
        if (!scan.HasPlatformNamespaces || !scan.HasPlatformPrefixedObjectElements)
        {
            return PlatformElementsBuild.PreparedXaml;
        }

        // The second preparation keeps those elements in the prepared XAML, when it works.
        if (reprepareRawElements && tryReprepare())
        {
            return PlatformElementsBuild.PreparedAgain;
        }

        // The document model that construction actions are generated from leaves them out.
        return PlatformElementsBuild.ConstructionActions;
    }

    private static KeyValuePair<string, string[]> Platform(string prefix, params string[] targets)
    {
        return new KeyValuePair<string, string[]>(prefix, targets);
    }

    private static bool IsIncluded(string platform, bool isNegated, string? targetPlatform)
    {
        string[]? targets = FindTargets(platform);
        return targets is not null && (Array.IndexOf(targets, NormalizeTarget(targetPlatform)) >= 0) != isNegated;
    }

    private static string[]? FindTargets(string platform)
    {
        for (int index = 0; index < _Platforms.Length; index++)
        {
            if (string.Equals(_Platforms[index].Key, platform, StringComparison.Ordinal))
            {
                return _Platforms[index].Value;
            }
        }

        return null;
    }

    private static string NormalizeTarget(string? targetPlatform)
    {
        if (targetPlatform is null || targetPlatform.Length == 0)
        {
            return DefaultTargetPlatform;
        }

        return string.Equals(targetPlatform, _WindowsAppSdk, StringComparison.Ordinal) ? _Windows : targetPlatform;
    }

    private static bool IsWindows(string? targetPlatform)
    {
        return string.Equals(NormalizeTarget(targetPlatform), _Windows, StringComparison.Ordinal);
    }

    private static bool IsClrNamespace(string namespaceUri)
    {
        return namespaceUri.StartsWith(XamlNamespaceUri.UsingScheme, StringComparison.Ordinal)
            || namespaceUri.StartsWith(_ClrNamespaceScheme, StringComparison.Ordinal);
    }
}
