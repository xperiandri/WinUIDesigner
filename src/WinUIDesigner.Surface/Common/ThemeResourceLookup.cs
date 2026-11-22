// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;

namespace WinUIDesigner.Surface.Common;

/// <summary>
/// What <see cref="ThemeResourceLookup"/> needs to know about a resource dictionary. The surface implements it on
/// WinUI's <c>ResourceDictionary</c>, the tests on a fake.
/// </summary>
internal interface IThemeResourceDictionaries<TDictionary>
    where TDictionary : class
{
    /// <summary>The dictionary's own entry for the key: not one of its theme dictionaries, not one it merges.</summary>
    bool TryGetOwnValue(TDictionary dictionary, object key, out object? value);

    /// <summary><c>ThemeDictionaries[themeKey]</c> when it is a dictionary, otherwise <see langword="null"/>.</summary>
    TDictionary? GetThemeDictionary(TDictionary dictionary, string themeKey);

    /// <summary>The dictionaries that the dictionary merges, in the order it merges them.</summary>
    IList<TDictionary> GetMergedDictionaries(TDictionary dictionary);

    /// <summary>
    /// The framework's own lookup (<c>TryGetValue</c>): it also answers from the theme dictionary of the dictionary's
    /// active theme, from the dictionaries it merges and, for some dictionaries, from the framework's own resources.
    /// </summary>
    bool TryGetFrameworkValue(TDictionary dictionary, object key, out object? value);
}

/// <summary>Where <see cref="ThemeResourceLookup.Resolve{TDictionary}"/> found a key.</summary>
/// <param name="Value">The value.</param>
/// <param name="ScopeIndex">The index of the scope that has it; -1 for the framework fallback.</param>
/// <param name="Path">The way from that scope to the entry, for the log.</param>
/// <param name="IsFrameworkFallback">Found only by the framework's own lookup, which answers in the application theme.</param>
internal readonly record struct ThemeResourceMatch(object? Value, int ScopeIndex, string Path, bool IsFrameworkFallback);

/// <summary>
/// Looks a <c>{ThemeResource}</c> key up for an explicit theme, one dictionary at a time, the way WinUI resolves a key:
/// a dictionary's own entries, then its theme dictionary for the theme, then the dictionaries it merges from the last to
/// the first, each of these searched the same way. A dictionary is never enumerated: that would create every deferred
/// resource of <c>XamlControlsResources</c>.
/// </summary>
internal static class ThemeResourceLookup
{
    /// <summary>The deepest nesting of theme and merged dictionaries that is searched; deeper ones are cut off.</summary>
    public const int MaximumDepth = 12;

    public const string FrameworkFallback = "framework fallback (application theme)";

    /// <summary>
    /// Looks <paramref name="key"/> up in <paramref name="scopes"/>, nearest first, for <paramref name="theme"/>. Only when
    /// every scope misses, the framework's own lookup on <paramref name="fallbackDictionary"/> answers; its answer is in
    /// the theme of the application, so the match says so.
    /// </summary>
    /// <exception cref="InvalidOperationException">The reference has no key.</exception>
    /// <exception cref="KeyNotFoundException">No scope has the key, and neither has the framework.</exception>
    public static ThemeResourceMatch Resolve<TDictionary>(
        IReadOnlyList<TDictionary> scopes,
        object? key,
        string theme,
        IThemeResourceDictionaries<TDictionary> dictionaries,
        TDictionary? fallbackDictionary)
        where TDictionary : class
    {
        // A reference without a key.
        if (key is null)
        {
            throw new InvalidOperationException("A ThemeResource reference has no resource key.");
        }

        IReadOnlyList<string> themeKeys = ThemeResourceThemes.GetThemeDictionaryKeys(theme);
        var searched = new HashSet<TDictionary>(ReferenceEqualityComparer.Instance);
        for (int index = 0; index < scopes.Count; index++)
        {
            // The nearest scope that has the key wins.
            if (TryFind(scopes[index], key, themeKeys, dictionaries, 0, searched, out object? value, out string path))
            {
                return new(value, index, path, IsFrameworkFallback: false);
            }
        }

        // No scope has it: the framework's own lookup answers, in the theme of the application.
        if (fallbackDictionary is not null && dictionaries.TryGetFrameworkValue(fallbackDictionary, key, out object? fallbackValue))
        {
            return new(fallbackValue, -1, FrameworkFallback, IsFrameworkFallback: true);
        }

        // Nobody has it.
        throw new KeyNotFoundException($"Unable to resolve ThemeResource '{key}' for the {theme} theme in the owning resource scope.");
    }

    /// <summary>
    /// Finds the own entry of a dictionary through the framework's lookup, which also answers from the dictionary's theme
    /// dictionary for its active theme, from the dictionaries it merges and possibly from the framework's own resources:
    /// what that lookup returns is an own entry when none of these other sources returns the same value. The framework's
    /// own resources are what it returns from <paramref name="emptyDictionary"/>, a dictionary without entries.
    /// </summary>
    /// <remarks>
    /// Values compare with <see cref="object.Equals(object, object)"/>, because the framework hands out a new box (or
    /// string) for a value on every lookup. An own entry whose value equals the value of another source counts as that
    /// source's, and the search goes on to the other sources.
    /// </remarks>
    public static bool TryGetOwnValueByElimination<TDictionary>(
        TDictionary dictionary,
        object key,
        IThemeResourceDictionaries<TDictionary> dictionaries,
        TDictionary? emptyDictionary,
        out object? value)
        where TDictionary : class
    {
        // The framework does not have the key for this dictionary at all.
        if (!dictionaries.TryGetFrameworkValue(dictionary, key, out value))
        {
            return false;
        }

        foreach (TDictionary source in GetOtherSources(dictionary, dictionaries, emptyDictionary))
        {
            // Another source gives the same value: the answer is that source's, not an entry of this dictionary.
            if (dictionaries.TryGetFrameworkValue(source, key, out object? other) && Equals(other, value))
            {
                value = null;
                return false;
            }
        }

        // No other source gives it: the dictionary's own entry.
        return true;
    }

    private static IEnumerable<TDictionary> GetOtherSources<TDictionary>(
        TDictionary dictionary,
        IThemeResourceDictionaries<TDictionary> dictionaries,
        TDictionary? emptyDictionary)
        where TDictionary : class
    {
        // Its theme dictionaries under every key that the surface knows: the framework answers from the active one.
        foreach (string themeKey in ThemeResourceThemes.KnownThemeDictionaryKeys)
        {
            if (dictionaries.GetThemeDictionary(dictionary, themeKey) is { } themeDictionary)
            {
                yield return themeDictionary;
            }
        }

        // The dictionaries it merges.
        foreach (TDictionary merged in dictionaries.GetMergedDictionaries(dictionary))
        {
            yield return merged;
        }

        // The framework's own resources, when it answers from them for any dictionary.
        if (emptyDictionary is not null)
        {
            yield return emptyDictionary;
        }
    }

    private static bool TryFind<TDictionary>(
        TDictionary dictionary,
        object key,
        IReadOnlyList<string> themeKeys,
        IThemeResourceDictionaries<TDictionary> dictionaries,
        int depth,
        HashSet<TDictionary> searched,
        out object? value,
        out string path)
        where TDictionary : class
    {
        value = null;
        path = string.Empty;

        // A dictionary that merges itself, one already searched for this key, or a chain deeper than any real one.
        if (depth > MaximumDepth || !searched.Add(dictionary))
        {
            return false;
        }

        // Its own entries. Whether WinUI prefers them over the theme dictionary of the same dictionary is not proven.
        if (dictionaries.TryGetOwnValue(dictionary, key, out value))
        {
            path = "own entry";
            return true;
        }

        // Its theme dictionary: WinUI chooses the dictionary first (the theme's own key, then Default), then the key in it.
        if (SelectThemeDictionary(dictionary, themeKeys, dictionaries, out string? themeKey) is { } themeDictionary
            && TryFind(themeDictionary, key, themeKeys, dictionaries, depth + 1, searched, out value, out string themePath))
        {
            path = $"theme dictionary '{themeKey}' > {themePath}";
            return true;
        }

        // The dictionaries it merges, from the last to the first.
        IList<TDictionary> merged = dictionaries.GetMergedDictionaries(dictionary);
        for (int index = merged.Count - 1; index >= 0; index--)
        {
            if (TryFind(merged[index], key, themeKeys, dictionaries, depth + 1, searched, out value, out string mergedPath))
            {
                path = FormattableString.Invariant($"merged dictionary {index} > {mergedPath}");
                return true;
            }
        }

        value = null;
        return false;
    }

    private static TDictionary? SelectThemeDictionary<TDictionary>(
        TDictionary dictionary,
        IReadOnlyList<string> themeKeys,
        IThemeResourceDictionaries<TDictionary> dictionaries,
        out string? themeKey)
        where TDictionary : class
    {
        foreach (string candidate in themeKeys)
        {
            // A ThemeDictionaries value that is not a dictionary does not count.
            if (dictionaries.GetThemeDictionary(dictionary, candidate) is { } themeDictionary)
            {
                themeKey = candidate;
                return themeDictionary;
            }
        }

        themeKey = null;
        return null;
    }
}
