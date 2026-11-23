// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;

namespace WinUIDesigner.Surface.Common;

/// <summary>What one scope of the walk from the owner of a <c>{ThemeResource}</c> says about the theme.</summary>
/// <param name="DictionaryTheme">
/// The theme of the <c>ThemeDictionaries</c> entry that the scope is (see
/// <see cref="ThemeResourceThemes.FromThemeDictionaryKey"/>), or <see langword="null"/>.
/// </param>
/// <param name="RequestedTheme">The <c>RequestedTheme</c> of the scope when it is an element that requests one.</param>
internal readonly record struct ThemeScopeFacts(string? DictionaryTheme, string? RequestedTheme);

/// <summary>The themes that the surface knows apart from the walk; <see langword="null"/> where it knows none.</summary>
/// <param name="SurfaceTheme">The preview theme of the surface of the owner's document.</param>
/// <param name="LastPreviewTheme">The theme of the last artboard colours that Visual Studio sent for any document.</param>
/// <param name="ApplicationRequestedTheme">The <c>RequestedTheme</c> of the project's <c>App.xaml</c>.</param>
/// <param name="ApplicationTheme">The theme of the application of the surface process, which follows Windows.</param>
internal readonly record struct ThemePreview(string? SurfaceTheme, string? LastPreviewTheme, string? ApplicationRequestedTheme, string ApplicationTheme);

/// <summary>What decided the theme of a <c>{ThemeResource}</c>.</summary>
internal enum ThemeChoiceSource
{
    /// <summary>The reference is inside a theme dictionary, which is used for that theme only.</summary>
    ThemeDictionary,

    /// <summary>The nearest element on the walk that requests a theme.</summary>
    RequestedTheme,

    /// <summary>The preview theme of the surface of the owner's document.</summary>
    Surface,

    /// <summary>The theme of the last artboard colours: the owner has no surface (yet).</summary>
    LastPreview,

    /// <summary>The <c>RequestedTheme</c> of the project's application.</summary>
    ApplicationRequestedTheme,

    /// <summary>The theme of the application of the surface process.</summary>
    Application,
}

/// <summary>The theme that a <c>{ThemeResource}</c> is evaluated in, and what decided it.</summary>
internal readonly record struct ThemeChoice(string Theme, ThemeChoiceSource Source);

/// <summary>
/// Decides the theme that a <c>{ThemeResource}</c> is evaluated in, and names the <c>ThemeDictionaries</c> keys that a
/// lookup tries for a theme. WinUI-free: themes and keys are the strings that <c>ThemeDictionaries</c> uses.
/// </summary>
internal static class ThemeResourceThemes
{
    public const string Light = "Light";
    public const string Dark = "Dark";
    public const string Default = "Default";
    public const string HighContrast = "HighContrast";

    /// <summary>Every key that the surface probes in a <c>ThemeDictionaries</c> map; the map is never enumerated.</summary>
    public static IReadOnlyList<string> KnownThemeDictionaryKeys { get; } = [Light, Dark, Default, HighContrast];

    /// <summary>
    /// The <c>ThemeDictionaries</c> keys that a lookup tries for <paramref name="theme"/>, in order: the theme's own key,
    /// then <c>Default</c>, which holds the dark values in WinUI's own dictionaries and is the fallback of every theme.
    /// </summary>
    public static IReadOnlyList<string> GetThemeDictionaryKeys(string theme) => theme == Default ? [Default] : [theme, Default];

    /// <summary>
    /// The theme that a reference stored in <c>ThemeDictionaries[key]</c> is evaluated in: that dictionary is used only
    /// while its theme is shown. <paramref name="hasKey"/> tells which other keys the same map has, because
    /// <c>Default</c> stands for the themes that have no dictionary of their own.
    /// </summary>
    public static string? FromThemeDictionaryKey(string? key, Func<string, bool> hasKey)
    {
        switch (key)
        {
            // A dictionary of a theme is used for that theme.
            case Light or Dark:
                return key;

            // Default serves the one theme that has no dictionary of its own; when it serves both, the preview decides.
            case Default:
                bool hasLight = hasKey(Light);
                bool hasDark = hasKey(Dark);
                return hasLight == hasDark ? null : hasLight ? Dark : Light;

            // HighContrast is never previewed, and other keys are not themes.
            default:
                return null;
        }
    }

    /// <summary>
    /// The theme of <paramref name="dictionary"/> when it is an entry of the <c>ThemeDictionaries</c> map
    /// <paramref name="themeDictionaries"/>, which is probed with the known keys and never enumerated.
    /// </summary>
    public static string? FromThemeDictionaries(object dictionary, IDictionary<object, object> themeDictionaries)
    {
        foreach (string key in KnownThemeDictionaryKeys)
        {
            // The map holds this very dictionary under that key: its theme is the one that the key stands for.
            if (themeDictionaries.TryGetValue(key, out object? value) && ReferenceEquals(value, dictionary))
            {
                return FromThemeDictionaryKey(key, themeDictionaries.ContainsKey);
            }
        }

        // Under none of the keys that the surface knows.
        return null;
    }

    /// <summary>
    /// The theme of the <c>ThemeDictionaries</c> entry that <paramref name="scope"/> is in <paramref name="parent"/>, the
    /// next scope of the walk. Visual Studio's search path goes from a theme dictionary to the <c>ThemeDictionaries</c> map
    /// and then to the dictionary that owns the map, so the parent is either. The map is probed with the known keys, never
    /// enumerated (see <see cref="FromThemeDictionaries"/>).
    /// </summary>
    /// <typeparam name="TDictionary">The resource dictionary type of the framework.</typeparam>
    /// <param name="scope">A scope of the walk.</param>
    /// <param name="parent">The scope after it.</param>
    /// <param name="themeDictionariesOf">The <c>ThemeDictionaries</c> map of a dictionary.</param>
    public static string? FromParent<TDictionary>(object scope, object parent, Func<TDictionary, IDictionary<object, object>> themeDictionariesOf)
        where TDictionary : class
    {
        // Not a dictionary: only a dictionary can be an entry of ThemeDictionaries.
        if (scope is not TDictionary dictionary)
        {
            return null;
        }

        // The dictionary that owns the map.
        if (parent is TDictionary owner && FromThemeDictionaries(dictionary, themeDictionariesOf(owner)) is { } theme)
        {
            return theme;
        }

        // The map itself. It can be a dictionary too, so a dictionary that did not match above is probed as a map.
        return parent is IDictionary<object, object> map ? FromThemeDictionaries(dictionary, map) : null;
    }

    /// <summary>
    /// Describes the walk from the owner of a reference: for each scope, the theme of the <c>ThemeDictionaries</c> entry
    /// that it is in its next scope (<paramref name="dictionaryTheme"/> gets the scope and its parent) and the theme it
    /// requests.
    /// </summary>
    public static List<ThemeScopeFacts> Describe<T>(
        T owner,
        Func<T, T?> parentOf,
        Func<T, T, string?> dictionaryTheme,
        Func<T, string?> requestedTheme)
        where T : class
    {
        var scopes = new List<T>(ResourceScopeWalk.Ancestors(owner, parentOf));
        var facts = new List<ThemeScopeFacts>(scopes.Count);
        for (int index = 0; index < scopes.Count; index++)
        {
            // The last scope has no next scope that it could be an entry of.
            string? theme = index + 1 < scopes.Count ? dictionaryTheme(scopes[index], scopes[index + 1]) : null;
            facts.Add(new(theme, requestedTheme(scopes[index])));
        }

        return facts;
    }

    /// <summary>
    /// Decides the theme of a <c>{ThemeResource}</c>: the theme of a theme dictionary that the walk passes, else the
    /// nearest requested theme, else the preview theme of the surface, else the theme of the last artboard colours, else
    /// the requested theme of the project's application, else the theme of the application of the process.
    /// </summary>
    public static ThemeChoice Decide(IEnumerable<ThemeScopeFacts> walk, ThemePreview preview)
    {
        string? requested = null;
        foreach (ThemeScopeFacts facts in walk)
        {
            // A reference in a theme dictionary is evaluated in the theme of that dictionary, whatever is shown around it.
            if (facts.DictionaryTheme is { } dictionaryTheme)
            {
                return new(dictionaryTheme, ThemeChoiceSource.ThemeDictionary);
            }

            // The nearest element that requests a theme decides for everything below it.
            requested ??= facts.RequestedTheme;
        }

        // An element on the walk requests a theme.
        if (requested is not null)
        {
            return new(requested, ThemeChoiceSource.RequestedTheme);
        }

        // Nothing on the walk requests one: what the surface shows around the document.
        if (preview.SurfaceTheme is { } surfaceTheme)
        {
            return new(surfaceTheme, ThemeChoiceSource.Surface);
        }

        // No surface yet (a document being built, a shared resource document): Visual Studio's artboard theme is global.
        if (preview.LastPreviewTheme is { } lastPreviewTheme)
        {
            return new(lastPreviewTheme, ThemeChoiceSource.LastPreview);
        }

        // No artboard colours yet: what the project's application asks for, else what the process shows.
        return preview.ApplicationRequestedTheme is { } applicationRequestedTheme
            ? new(applicationRequestedTheme, ThemeChoiceSource.ApplicationRequestedTheme)
            : new(preview.ApplicationTheme, ThemeChoiceSource.Application);
    }

    /// <summary>
    /// Whether a property of an action (<c>Name:DeclaringType</c>) is <c>RequestedTheme</c>, whose change moves the
    /// theme of the <c>{ThemeResource}</c> references below it.
    /// </summary>
    public static bool IsRequestedThemeProperty(string? fullPropertyName)
    {
        // An action without a property.
        if (fullPropertyName is null)
        {
            return false;
        }

        ReadOnlySpan<char> name = fullPropertyName.AsSpan();
        int separator = name.IndexOf(':');
        return (separator < 0 ? name : name[..separator]).Trim() is "RequestedTheme";
    }
}
