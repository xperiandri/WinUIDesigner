// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;

namespace WinUIDesigner.Surface.Services;

/// <summary>
/// Resolves static resources within the document and its theme resource scopes.
/// </summary>
internal sealed class ResourceScopeService(ObjectIdentityRegistry identity)
{
    private readonly Dictionary<object, object> _parents = new(ReferenceEqualityComparer.Instance);

    private string? _themeAssembly;

    private string? _themePath;

    private ResourceDictionary? _themeResources;

    private readonly ResourceDictionaries _dictionaries = new();

    public void SetParent(object child, object? parent)
    {
        if (parent is null)
        {
            _parents.Remove(child);

            return;
        }

        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance) { child };
        for (object? current = parent; current is not null; current = _parents.GetValueOrDefault(current))
        {
            if (!visited.Add(current))
            {
                throw new InvalidOperationException("The resource search scope contains a cycle.");
            }
        }

        _parents[child] = parent;
    }

    public void SetThemeScope(string assembly, string relativePath)
    {
        if (_themeAssembly == assembly && _themePath == relativePath)
        {
            return;
        }

        _themeAssembly = assembly;
        _themePath = relativePath;
        _themeResources = string.IsNullOrEmpty(relativePath)
            ? null
            : new ResourceDictionary() { Source = new Uri($"ms-appx:///{assembly.Split(',')[0]}/{relativePath.TrimStart('/')}") };
    }

    public object? ResolveStaticResource(object owner, object? key)
    {
        if (key is null)
        {
            throw new InvalidOperationException("A StaticResource reference has no resource key.");
        }

        // The scopes of the owner, the search parents that Visual Studio sent first, then the application, then the theme.
        IEnumerable<ResourceDictionary> dictionaries = ResourceScopeWalk.StaticResourceDictionaries(
            owner,
            _parents,
            GetLiveParent,
            GetScopeDictionary,
            Application.Current.Resources,
            _themeResources
        );
        foreach (ResourceDictionary dictionary in dictionaries)
        {
            if (dictionary.TryGetValue(key, out object? value))
            {
                return value;
            }
        }

        throw new KeyNotFoundException($"Unable to resolve StaticResource '{key}' in the owning resource scope.");
    }

    /// <summary>
    /// Decides the theme that a <c>{ThemeResource}</c> set on <paramref name="owner"/> is evaluated in, from the walk
    /// of its scopes and <paramref name="preview"/>; see <see cref="ThemeResourceThemes.Decide"/>.
    /// </summary>
    public ThemeChoice DecideTheme(object owner, ThemePreview preview)
    {
        List<ThemeScopeFacts> walk = ThemeResourceThemes.Describe(owner, GetThemeResourceParent, GetDictionaryTheme, GetRequestedTheme);
        return ThemeResourceThemes.Decide(walk, preview);
    }

    /// <summary>
    /// Resolves a <c>{ThemeResource}</c> key for <paramref name="theme"/> in the scopes of <paramref name="owner"/> (the
    /// live parents of attached objects first), then in the application's resources; see
    /// <see cref="ThemeResourceLookup.Resolve{TDictionary}"/>.
    /// </summary>
    public ThemeResourceMatch ResolveThemeResource(object owner, object? key, string theme)
    {
        ResourceDictionary application = Application.Current.Resources;
        IEnumerable<ResourceDictionary> dictionaries = ResourceScopeWalk.ThemeResourceDictionaries(
            owner,
            _parents,
            GetLiveParent,
            GetScopeDictionary,
            application,
            _themeResources
        );
        return ThemeResourceLookup.Resolve(new List<ResourceDictionary>(dictionaries), key, theme, _dictionaries, application);
    }

    /// <summary>Names the scope of a <see cref="ThemeResourceMatch"/> for the log.</summary>
    public string DescribeThemeResourceScope(object owner, ThemeResourceMatch match)
    {
        // Not found in a scope.
        if (match.ScopeIndex < 0)
        {
            return "the framework";
        }

        int index = 0;
        foreach (object scope in ResourceScopeWalk.Ancestors(owner, GetThemeResourceParent))
        {
            // A scope of the walk: an element's resources or a dictionary.
            if (GetScopeDictionary(scope) is not null && index++ == match.ScopeIndex)
            {
                return scope is ResourceDictionary ? "a resource dictionary" : $"the resources of {scope.GetType().Name}";
            }
        }

        // The scopes that come after the walk.
        return index == match.ScopeIndex ? "Application.Resources" : "the theme resources";
    }

    /// <summary>The name of an element theme in <c>ThemeDictionaries</c>; <see langword="null"/> for the default.</summary>
    public static string? ToThemeName(ElementTheme theme) => theme switch
    {
        ElementTheme.Light => ThemeResourceThemes.Light,
        ElementTheme.Dark => ThemeResourceThemes.Dark,
        _ => null,
    };

    public object Parse(object? owner, string xaml)
    {
        var application = Application.Current.Resources;

        var dictionaries = new List<ResourceDictionary>();
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        for (object? current = owner; current is not null && visited.Add(current); current = _parents.GetValueOrDefault(current))
        {
            var dictionary = current switch
            {
                FrameworkElement element => element.Resources,
                ResourceDictionary resources => resources,
                Application app => app.Resources,
                _ => null,
            };

            if (dictionary is not null && !ReferenceEquals(dictionary, application) && !dictionaries.Contains(dictionary))
            {
                dictionaries.Add(dictionary);
            }
        }
        // The innermost scope must win over ancestors while parsing detached objects.

        dictionaries.Reverse();

        if (_themeResources is not null && !dictionaries.Contains(_themeResources))
        {
            dictionaries.Insert(0, _themeResources);
        }

        int originalCount = application.MergedDictionaries.Count;

        try
        {
            foreach (ResourceDictionary dictionary in dictionaries)
            {
                application.MergedDictionaries.Add(dictionary);
            }

            return XamlReader.Load(xaml) ?? throw new InvalidOperationException("XAML parsing returned no object.");
        }
        finally
        {
            while (application.MergedDictionaries.Count > originalCount)
            {
                application.MergedDictionaries.RemoveAt(application.MergedDictionaries.Count - 1);
            }
        }
    }

    public void UpdateResources(object owner)
    {
        var dictionary = owner switch
        {
            ResourceDictionary resources => resources,
            FrameworkElement element => element.Resources,
            Application application => application.Resources,
            _ => null,
        } ?? throw new InvalidOperationException("The resource owner has no dictionary.");

        foreach (var entry in dictionary)
        {
            if (entry.Value is not null)
            {
                identity.Track(entry.Value);
            }
        }
    }

    public void RemoveObjects(HashSet<object> released)
    {
        foreach (object key in new List<object>(_parents.Keys))
        {
            if (released.Contains(key) || released.Contains(_parents[key]))
            {
                _parents.Remove(key);
            }
        }
    }

    private static object? GetLiveParent(object current) => (current as FrameworkElement)?.Parent;

    private object? GetThemeResourceParent(object current) => ResourceScopeWalk.LiveParentFirst(current, _parents, GetLiveParent);

    private static ResourceDictionary? GetScopeDictionary(object scope) => scope switch
    {
        FrameworkElement element => element.Resources,
        ResourceDictionary resources => resources,
        Application app => app.Resources,
        _ => null,
    };

    private static string? GetRequestedTheme(object scope)
    {
        return scope is FrameworkElement element ? ToThemeName(element.RequestedTheme) : null;
    }

    /// <summary>
    /// The theme of the <c>ThemeDictionaries</c> entry that <paramref name="scope"/> is in <paramref name="parent"/>.
    /// Visual Studio's search path goes from a theme dictionary to the <c>ThemeDictionaries</c> map and then to the
    /// dictionary that owns the map, so the parent is either. The map is probed with the known keys, never enumerated.
    /// </summary>
    private static string? GetDictionaryTheme(object scope, object parent)
    {
        if (scope is not ResourceDictionary dictionary)
        {
            return null;
        }

        // The dictionary that owns the map.
        if (parent is ResourceDictionary owner && ThemeResourceThemes.FromThemeDictionaries(dictionary, owner.ThemeDictionaries) is { } theme)
        {
            return theme;
        }

        // The map itself.
        return parent is IDictionary<object, object> map ? ThemeResourceThemes.FromThemeDictionaries(dictionary, map) : null;
    }

    /// <summary>WinUI's resource dictionaries for <see cref="ThemeResourceLookup"/>.</summary>
    private sealed class ResourceDictionaries : IThemeResourceDictionaries<ResourceDictionary>
    {
        // A dictionary without entries: what the framework returns from it comes from the framework's own resources.
        private ResourceDictionary? _empty;

        public bool TryGetOwnValue(ResourceDictionary dictionary, object key, out object? value)
        {
            // The public lookup answers from the theme and merged dictionaries too (ResolveStaticResource relies on it),
            // so an own entry is what none of the other sources answers.
            _empty ??= new ResourceDictionary();
            return ThemeResourceLookup.TryGetOwnValueByElimination(dictionary, key, this, _empty, out value);
        }

        public ResourceDictionary? GetThemeDictionary(ResourceDictionary dictionary, string themeKey)
        {
            return dictionary.ThemeDictionaries.TryGetValue(themeKey, out object? themed) ? themed as ResourceDictionary : null;
        }

        public IList<ResourceDictionary> GetMergedDictionaries(ResourceDictionary dictionary) => dictionary.MergedDictionaries;

        public bool TryGetFrameworkValue(ResourceDictionary dictionary, object key, out object? value)
        {
            return dictionary.TryGetValue(key, out value);
        }
    }
}
