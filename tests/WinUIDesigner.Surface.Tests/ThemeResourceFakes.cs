// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using WinUIDesigner.Surface.Common;

/// <summary>An element of a fake tree: a theme it may request, and resources.</summary>
internal sealed class FakeElement(string name, string? requestedTheme = null)
{
    public string Name { get; } = name;

    public string? RequestedTheme { get; set; } = requestedTheme;

    public FakeDictionary Resources { get; init; } = new($"{name}.Resources");

    public override string ToString() => Name;
}

/// <summary>
/// What the surface reads from the objects of a scope walk, for the fakes: the dictionary of a scope, the theme that it
/// requests, and the theme of a dictionary that is an entry of a <c>ThemeDictionaries</c> map.
/// </summary>
internal static class FakeScopes
{
    /// <summary>
    /// As the surface does, with the same function: the parent is the <c>ThemeDictionaries</c> map, or the dictionary that
    /// owns it.
    /// </summary>
    public static string? DictionaryTheme(object scope, object parent)
    {
        return ThemeResourceThemes.FromParent<FakeDictionary>(scope, parent, owner => owner.ThemeDictionaries);
    }

    public static string? RequestedTheme(object scope) => (scope as FakeElement)?.RequestedTheme;

    public static FakeDictionary? DictionaryOf(object scope) => scope switch
    {
        FakeElement element => element.Resources,
        FakeDictionary dictionary => dictionary,
        _ => null,
    };
}

/// <summary>
/// A resource dictionary as the theme lookup sees one: own entries, <c>ThemeDictionaries</c> and merged dictionaries.
/// <see cref="TryGetFrameworkValue(object, out object)"/> stands for WinUI's own lookup as the surface assumes it works: own entries, then the
/// theme dictionary of the active theme (the dictionary is chosen first: the theme's key, then Default), then the merged
/// dictionaries from the last to the first, and for a dictionary with <see cref="FrameworkResources"/> those last.
/// </summary>
internal sealed class FakeDictionary(string name)
{
    public string Name => name;

    public Dictionary<object, object?> Own { get; } = [];

    public Dictionary<object, object> ThemeDictionaries { get; } = [];

    public List<FakeDictionary> Merged { get; } = [];

    /// <summary>The theme that the framework's lookup uses: the application's, not the preview's.</summary>
    public string ActiveTheme { get; set; } = ThemeResourceThemes.Light;

    /// <summary>The framework's own resources, which its lookup answers from when nothing else has the key.</summary>
    public Dictionary<object, object?>? FrameworkResources { get; set; }

    /// <summary>Returns the theme dictionary under <paramref name="key"/>, created on first use.</summary>
    public FakeDictionary Theme(string key)
    {
        if (!ThemeDictionaries.TryGetValue(key, out object? themed) || themed is not FakeDictionary dictionary)
        {
            ThemeDictionaries[key] = dictionary = new FakeDictionary($"{name}[{key}]");
        }

        return dictionary;
    }

    public FakeDictionary Merge(FakeDictionary merged)
    {
        Merged.Add(merged);
        return this;
    }

    public bool TryGetFrameworkValue(object key, out object? value)
    {
        return TryGetFrameworkValue(key, ActiveTheme, new HashSet<FakeDictionary>(ReferenceEqualityComparer.Instance), out value);
    }

    public override string ToString() => name;

    private bool TryGetFrameworkValue(object key, string theme, HashSet<FakeDictionary> searched, out object? value)
    {
        value = null;
        if (!searched.Add(this))
        {
            return false;
        }

        if (Own.TryGetValue(key, out value))
        {
            return true;
        }

        foreach (string themeKey in ThemeResourceThemes.GetThemeDictionaryKeys(theme))
        {
            if (ThemeDictionaries.TryGetValue(themeKey, out object? themed) && themed is FakeDictionary themeDictionary)
            {
                if (themeDictionary.TryGetFrameworkValue(key, theme, searched, out value))
                {
                    return true;
                }

                break;
            }
        }

        for (int index = Merged.Count - 1; index >= 0; index--)
        {
            if (Merged[index].TryGetFrameworkValue(key, theme, searched, out value))
            {
                return true;
            }
        }

        return FrameworkResources is not null && FrameworkResources.TryGetValue(key, out value);
    }
}

/// <summary>
/// The view of <see cref="FakeDictionary"/> for <see cref="ThemeResourceLookup"/>. With <paramref name="ownEntriesThroughFramework"/>
/// it finds own entries the way the surface does on WinUI, by elimination through the framework's lookup; without, it
/// reads them directly.
/// </summary>
internal sealed class FakeDictionaries(bool ownEntriesThroughFramework, FakeDictionary? emptyDictionary = null)
    : IThemeResourceDictionaries<FakeDictionary>
{
    /// <summary>How often the lookup asked for the own entry of each dictionary: once for every dictionary it searched.</summary>
    public Dictionary<FakeDictionary, int> OwnEntryLookups { get; } = new(ReferenceEqualityComparer.Instance);

    public bool TryGetOwnValue(FakeDictionary dictionary, object key, out object? value)
    {
        OwnEntryLookups[dictionary] = OwnEntryLookups.GetValueOrDefault(dictionary) + 1;
        return ownEntriesThroughFramework
            ? ThemeResourceLookup.TryGetOwnValueByElimination(dictionary, key, this, emptyDictionary, out value)
            : dictionary.Own.TryGetValue(key, out value);
    }

    public FakeDictionary? GetThemeDictionary(FakeDictionary dictionary, string themeKey)
    {
        return dictionary.ThemeDictionaries.TryGetValue(themeKey, out object? themed) ? themed as FakeDictionary : null;
    }

    public IList<FakeDictionary> GetMergedDictionaries(FakeDictionary dictionary) => dictionary.Merged;

    public bool TryGetFrameworkValue(FakeDictionary dictionary, object key, out object? value)
    {
        return dictionary.TryGetFrameworkValue(key, out value);
    }
}
