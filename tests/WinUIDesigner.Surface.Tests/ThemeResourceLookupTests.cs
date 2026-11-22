// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using WinUIDesigner.Surface.Common;

/// <summary>
/// The lookup of <c>{ThemeResource}</c> keys for an explicit theme. Each scope test runs twice: with own entries read
/// directly, and with own entries found by elimination through the framework's lookup, as the surface does on WinUI.
/// </summary>
[TestClass]
public sealed class ThemeResourceLookupTests
{
    private const string _Key = "CardBackgroundFillColorDefaultBrush";

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void DarkPreviewTakesDefaultWhenThereIsNoDark(bool throughFramework)
    {
        ThemeResourceMatch match = Resolve(throughFramework, ThemeResourceThemes.Dark, _Key, Application(ControlsResources()));

        Assert.AreEqual("card dark", match.Value);
        Assert.Contains("theme dictionary 'Default'", match.Path);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void DarkPreviewPrefersDarkOverDefault(bool throughFramework)
    {
        FakeDictionary controls = ControlsResources();
        controls.Theme(ThemeResourceThemes.Dark).Own[_Key] = "card dark of its own";

        Assert.AreEqual("card dark of its own", Resolve(throughFramework, ThemeResourceThemes.Dark, _Key, Application(controls)).Value);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void LightPreviewFallsBackToDefault(bool throughFramework)
    {
        // An app palette that only says Default (and HighContrast) serves the light theme from Default as well.
        var palette = new FakeDictionary("Palette");
        palette.Theme(ThemeResourceThemes.Default).Own["AccentBrush"] = "accent";
        palette.Theme(ThemeResourceThemes.HighContrast).Own["AccentBrush"] = "accent high contrast";

        ThemeResourceMatch match = Resolve(throughFramework, ThemeResourceThemes.Light, "AccentBrush", Application(palette));

        // The fake framework would answer the same from its lookup, so the lookup itself must have found it.
        Assert.AreEqual("accent", match.Value);
        Assert.IsFalse(match.IsFrameworkFallback);
        Assert.Contains("theme dictionary 'Default'", match.Path);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void PreviewThemeWinsOverTheFrameworksActiveTheme(bool throughFramework)
    {
        // The application of the surface process is light; the preview is dark.
        FakeDictionary application = Application(ControlsResources());
        Assert.IsTrue(application.TryGetFrameworkValue(_Key, out object? frameworkValue));
        Assert.AreEqual("card light", frameworkValue);

        Assert.AreEqual("card dark", Resolve(throughFramework, ThemeResourceThemes.Dark, _Key, application).Value);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ThemeDictionariesOfMergedDictionariesAreSearchedLastFirst(bool throughFramework)
    {
        // Like XamlControlsResources, whose content merges the dictionaries that hold the theme resources.
        var first = new FakeDictionary("First");
        first.Theme(ThemeResourceThemes.Default).Own[_Key] = "first dark";
        var nested = new FakeDictionary("Nested");
        nested.Theme(ThemeResourceThemes.Default).Own[_Key] = "second dark";
        FakeDictionary second = new FakeDictionary("Second").Merge(nested);

        ThemeResourceMatch match = Resolve(throughFramework, ThemeResourceThemes.Dark, _Key, Application(first, second));

        Assert.AreEqual("second dark", match.Value);
        Assert.AreEqual("merged dictionary 1 > merged dictionary 0 > theme dictionary 'Default' > own entry", match.Path);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void InnerScopeWinsOverApplication(bool throughFramework)
    {
        var page = new FakeDictionary("Page");
        page.Theme(ThemeResourceThemes.Default).Own[_Key] = "page dark";

        ThemeResourceMatch match = Resolve(throughFramework, ThemeResourceThemes.Dark, _Key, page, Application(ControlsResources()));

        Assert.AreEqual("page dark", match.Value);
        Assert.AreEqual(0, match.ScopeIndex);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void InnerScopeWinsWhateverTheEntryKind(bool throughFramework)
    {
        // An inner plain entry against an outer theme entry.
        var innerPlain = new FakeDictionary("Inner plain");
        innerPlain.Own[_Key] = "inner plain";
        Assert.AreEqual("inner plain", Resolve(throughFramework, ThemeResourceThemes.Dark, _Key, innerPlain, Application(ControlsResources())).Value);

        // An inner theme entry against an outer plain entry.
        var innerTheme = new FakeDictionary("Inner theme");
        innerTheme.Theme(ThemeResourceThemes.Default).Own[_Key] = "inner dark";
        var outerPlain = new FakeDictionary("Outer plain");
        outerPlain.Own[_Key] = "outer plain";
        Assert.AreEqual("inner dark", Resolve(throughFramework, ThemeResourceThemes.Dark, _Key, innerTheme, outerPlain).Value);
    }

    [TestMethod]
    [DataRow(false, ThemeResourceThemes.Light)]
    [DataRow(false, ThemeResourceThemes.Dark)]
    [DataRow(true, ThemeResourceThemes.Light)]
    [DataRow(true, ThemeResourceThemes.Dark)]
    public void LaterMergedPlainEntryWinsOverEarlierMergedThemeEntry(bool throughFramework, string theme)
    {
        // One dictionary at a time: a lookup that searched the theme dictionaries of the whole tree first would return
        // the brush of the first dictionary.
        var themed = new FakeDictionary("Themed");
        themed.Theme(ThemeResourceThemes.Default).Own[_Key] = "themed dark";
        themed.Theme(ThemeResourceThemes.Light).Own[_Key] = "themed light";
        var plain = new FakeDictionary("Plain");
        plain.Own[_Key] = "plain";

        Assert.AreEqual("plain", Resolve(throughFramework, theme, _Key, Application(themed, plain)).Value);
    }

    [TestMethod]
    [DataRow(false, ThemeResourceThemes.Light)]
    [DataRow(false, ThemeResourceThemes.Dark)]
    [DataRow(true, ThemeResourceThemes.Light)]
    [DataRow(true, ThemeResourceThemes.Dark)]
    public void OwnPlainEntryWinsOverThemeEntryOfMergedDictionary(bool throughFramework, string theme)
    {
        // App.xaml overrides a brush of XamlControlsResources, which it merges, with a plain entry.
        FakeDictionary appXaml = new FakeDictionary("App.xaml").Merge(ControlsResources());
        appXaml.Own[_Key] = "override";

        Assert.AreEqual("override", Resolve(throughFramework, theme, _Key, Application(appXaml)).Value);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ThemeDictionaryIsChosenBeforeTheKey(bool throughFramework)
    {
        // The dictionary has Light without the key and Default with it: for the light theme it has no match at all.
        var dictionary = new FakeDictionary("Page");
        dictionary.Theme(ThemeResourceThemes.Light).Own["Other"] = "other";
        dictionary.Theme(ThemeResourceThemes.Default).Own[_Key] = "page default";
        var outer = new FakeDictionary("Outer");
        outer.Own[_Key] = "outer";

        Assert.AreEqual("outer", Resolve(throughFramework, ThemeResourceThemes.Light, _Key, dictionary, outer).Value);
        Assert.AreEqual("page default", Resolve(throughFramework, ThemeResourceThemes.Dark, _Key, dictionary, outer).Value);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void AThemeDictionaryValueThatIsNotADictionaryIsSkipped(bool throughFramework)
    {
        var dictionary = new FakeDictionary("Page");
        dictionary.ThemeDictionaries[ThemeResourceThemes.Light] = "not a dictionary";
        dictionary.Theme(ThemeResourceThemes.Default).Own[_Key] = "page default";

        ThemeResourceMatch match = Resolve(throughFramework, ThemeResourceThemes.Light, _Key, dictionary);

        // The fake framework would answer the same from its lookup, so the lookup itself must have found it.
        Assert.AreEqual("page default", match.Value);
        Assert.IsFalse(match.IsFrameworkFallback);
        Assert.AreEqual("theme dictionary 'Default' > own entry", match.Path);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void OwnEntryComesBeforeTheThemeDictionaryOfTheSameDictionary(bool throughFramework)
    {
        // The order that this lookup chose; WinUI's order for a key defined both ways in one dictionary is not proven.
        var dictionary = new FakeDictionary("Page");
        dictionary.Own[_Key] = "own";
        dictionary.Theme(ThemeResourceThemes.Default).Own[_Key] = "theme";

        ThemeResourceMatch match = Resolve(throughFramework, ThemeResourceThemes.Dark, _Key, dictionary);

        Assert.AreEqual("own", match.Value);
        Assert.AreEqual("own entry", match.Path);
    }

    [TestMethod]
    public void MissingKeyThrowsWithKeyAndTheme()
    {
        KeyNotFoundException exception = Assert.Throws<KeyNotFoundException>(
            () => Resolve(false, ThemeResourceThemes.Dark, "MissingBrush", Application(ControlsResources()))
        );

        Assert.Contains("ThemeResource 'MissingBrush'", exception.Message);
        Assert.Contains("Dark theme", exception.Message);
    }

    [TestMethod]
    public void AReferenceWithoutAKeyIsRejected()
    {
        Assert.Throws<InvalidOperationException>(() => ThemeResourceLookup.Resolve(
            [new FakeDictionary("Page")],
            null,
            ThemeResourceThemes.Dark,
            new FakeDictionaries(false),
            null
        ));
    }

    [TestMethod]
    public void FrameworkFallbackIsMarked()
    {
        // A key that only the framework's own resources have, such as a system colour: answered in the application theme.
        FakeDictionary application = Application(ControlsResources());
        application.FrameworkResources = new() { ["SystemAccentColorLight1"] = "accent" };

        ThemeResourceMatch fallback = Resolve(false, ThemeResourceThemes.Dark, "SystemAccentColorLight1", new FakeDictionary("Page"), application);
        ThemeResourceMatch found = Resolve(false, ThemeResourceThemes.Dark, _Key, new FakeDictionary("Page"), application);

        Assert.AreEqual("accent", fallback.Value);
        Assert.IsTrue(fallback.IsFrameworkFallback);
        Assert.AreEqual(-1, fallback.ScopeIndex);
        Assert.AreEqual(ThemeResourceLookup.FrameworkFallback, fallback.Path);
        Assert.IsFalse(found.IsFrameworkFallback);
    }

    [TestMethod]
    public void SelfMergingDictionariesTerminate()
    {
        // A dictionary that merges itself, directly and through its theme dictionary, is searched once. Only the depth
        // limit would end a lookup that searched it again every time it met it, after 80,781 searches.
        var dictionary = new FakeDictionary("Self");
        dictionary.Merge(dictionary).Merge(dictionary);
        FakeDictionary themeDictionary = dictionary.Theme(ThemeResourceThemes.Default).Merge(dictionary);
        var dictionaries = new FakeDictionaries(false);

        Assert.Throws<KeyNotFoundException>(
            () => ThemeResourceLookup.Resolve([dictionary], _Key, ThemeResourceThemes.Dark, dictionaries, null)
        );

        Assert.AreEqual(1, dictionaries.OwnEntryLookups[dictionary]);
        Assert.AreEqual(1, dictionaries.OwnEntryLookups[themeDictionary]);
    }

    [TestMethod]
    public void ADictionaryThatCanBeReachedTwiceIsSearchedOnce()
    {
        // Top merges Left and Right, and both merge Shared.
        var shared = new FakeDictionary("Shared");
        FakeDictionary left = new FakeDictionary("Left").Merge(shared);
        FakeDictionary right = new FakeDictionary("Right").Merge(shared);
        FakeDictionary top = new FakeDictionary("Top").Merge(left).Merge(right);
        var dictionaries = new FakeDictionaries(false);

        Assert.Throws<KeyNotFoundException>(
            () => ThemeResourceLookup.Resolve([top], _Key, ThemeResourceThemes.Dark, dictionaries, null)
        );

        Assert.AreEqual(1, dictionaries.OwnEntryLookups[shared]);
    }

    [TestMethod]
    public void ADictionaryThatTwoScopesShareIsSearchedOnce()
    {
        // The page and the application merge the same dictionary: what the page's search missed, the application's would miss.
        var shared = new FakeDictionary("Shared");
        FakeDictionary page = new FakeDictionary("Page").Merge(shared);
        FakeDictionary application = new FakeDictionary("Application.Resources").Merge(shared);
        var dictionaries = new FakeDictionaries(false);

        Assert.Throws<KeyNotFoundException>(
            () => ThemeResourceLookup.Resolve([page, application], _Key, ThemeResourceThemes.Dark, dictionaries, null)
        );

        Assert.AreEqual(1, dictionaries.OwnEntryLookups[shared]);
    }

    [TestMethod]
    [DataRow(ThemeResourceLookup.MaximumDepth, true)]
    [DataRow(ThemeResourceLookup.MaximumDepth + 1, false)]
    public void DeepDictionariesAreCutOffAtTheMaximumDepth(int depth, bool found)
    {
        var root = new FakeDictionary("Depth 0");
        FakeDictionary current = root;
        for (int level = 1; level <= depth; level++)
        {
            var merged = new FakeDictionary($"Depth {level}");
            current.Merge(merged);
            current = merged;
        }

        current.Own[_Key] = "deep";

        // Without a framework fallback, which would search the whole depth.
        ThemeResourceMatch Find() => ThemeResourceLookup.Resolve([root], _Key, ThemeResourceThemes.Dark, new FakeDictionaries(false), null);
        if (found)
        {
            Assert.AreEqual("deep", Find().Value);
        }
        else
        {
            Assert.Throws<KeyNotFoundException>(() => Find());
        }
    }

    [TestMethod]
    public void EliminationFindsAnOwnEntryThatShadowsAMergedOne()
    {
        var merged = new FakeDictionary("Merged");
        merged.Own[_Key] = "merged";
        FakeDictionary dictionary = new FakeDictionary("Page").Merge(merged);
        dictionary.Own[_Key] = "own";

        Assert.IsTrue(ThemeResourceLookup.TryGetOwnValueByElimination(dictionary, _Key, new FakeDictionaries(true), null, out object? value));
        Assert.AreEqual("own", value);
    }

    [TestMethod]
    public void EliminationDoesNotTakeAMergedOrThemeEntryForAnOwnOne()
    {
        var merged = new FakeDictionary("Merged");
        merged.Own["MergedKey"] = "merged";
        FakeDictionary dictionary = new FakeDictionary("Page").Merge(merged);
        dictionary.Theme(ThemeResourceThemes.Light).Own["ThemedKey"] = "light";
        var dictionaries = new FakeDictionaries(true);

        Assert.IsFalse(ThemeResourceLookup.TryGetOwnValueByElimination(dictionary, "MergedKey", dictionaries, null, out _));
        Assert.IsFalse(ThemeResourceLookup.TryGetOwnValueByElimination(dictionary, "ThemedKey", dictionaries, null, out _));
        Assert.IsFalse(ThemeResourceLookup.TryGetOwnValueByElimination(dictionary, "MissingKey", dictionaries, null, out _));
    }

    [TestMethod]
    public void EliminationDoesNotTakeTheFrameworksOwnResourcesForAnOwnEntry()
    {
        // A framework whose lookup answers from its own resources in every dictionary: the empty dictionary shows it.
        Dictionary<object, object?> frameworkResources = new() { ["SystemAccentColor"] = "accent" };
        var page = new FakeDictionary("Page") { FrameworkResources = frameworkResources };
        var empty = new FakeDictionary("Empty") { FrameworkResources = frameworkResources };
        var dictionaries = new FakeDictionaries(true);

        bool withoutProbe = ThemeResourceLookup.TryGetOwnValueByElimination(page, "SystemAccentColor", dictionaries, null, out _);
        bool withProbe = ThemeResourceLookup.TryGetOwnValueByElimination(page, "SystemAccentColor", dictionaries, empty, out _);

        Assert.IsTrue(withoutProbe, "Without the empty dictionary the framework's answer looks like an own entry.");
        Assert.IsFalse(withProbe);
    }

    private static ThemeResourceMatch Resolve(bool throughFramework, string theme, object key, params FakeDictionary[] scopes)
    {
        // As in the surface: the framework fallback is the lookup of the last scope, the application's dictionary.
        return ThemeResourceLookup.Resolve(scopes, key, theme, new FakeDictionaries(throughFramework), scopes[^1]);
    }

    /// <summary>Like XamlControlsResources: the dark values under Default, the light ones under Light.</summary>
    private static FakeDictionary ControlsResources()
    {
        var controls = new FakeDictionary("XamlControlsResources");
        controls.Theme(ThemeResourceThemes.Default).Own[_Key] = "card dark";
        controls.Theme(ThemeResourceThemes.Light).Own[_Key] = "card light";
        controls.Theme(ThemeResourceThemes.HighContrast).Own[_Key] = "card high contrast";
        return controls;
    }

    /// <summary>Like the surface's Application.Resources: no entries of its own, the project's dictionaries merged.</summary>
    private static FakeDictionary Application(params FakeDictionary[] merged)
    {
        var application = new FakeDictionary("Application.Resources");
        application.Merged.AddRange(merged);
        return application;
    }
}
