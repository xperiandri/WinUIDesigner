// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using WinUIDesigner.Surface.Common;

/// <summary>The theme that a <c>{ThemeResource}</c> is evaluated in.</summary>
[TestClass]
public sealed class ThemeResourceThemesTests
{
    private const string _Light = ThemeResourceThemes.Light;
    private const string _Dark = ThemeResourceThemes.Dark;
    private const string _Default = ThemeResourceThemes.Default;

    [TestMethod]
    public void ClosestExplicitRequestedThemeWins()
    {
        ThemeChoice choice = ThemeResourceThemes.Decide([new(null, null), new(null, _Dark), new(null, _Light)], new(_Light, _Light, null, _Light));

        Assert.AreEqual(new ThemeChoice(_Dark, ThemeChoiceSource.RequestedTheme), choice);
    }

    [TestMethod]
    public void ReferenceInsideThemeDictionaryUsesItsKey()
    {
        // The theme dictionary is farther from the reference than an element that requests the dark theme, and it is
        // used only while its theme is shown.
        ThemeChoice choice = ThemeResourceThemes.Decide([new(null, _Dark), new(_Light, null), new(null, _Dark)], new(_Dark, _Dark, null, _Dark));

        Assert.AreEqual(new ThemeChoice(_Light, ThemeChoiceSource.ThemeDictionary), choice);
    }

    [TestMethod]
    public void DefaultUsesPreviewTheme()
    {
        ThemeChoice choice = ThemeResourceThemes.Decide([new(null, null), new(null, null)], new(_Dark, _Light, _Light, _Light));

        Assert.AreEqual(new ThemeChoice(_Dark, ThemeChoiceSource.Surface), choice);
    }

    [TestMethod]
    public void DefaultPreviewUsesLastArtboardTheme()
    {
        // A document that is being built has no surface yet; the artboard colours of another document say the theme.
        ThemeChoice choice = ThemeResourceThemes.Decide([new(null, null)], new(null, _Dark, _Light, _Light));

        Assert.AreEqual(new ThemeChoice(_Dark, ThemeChoiceSource.LastPreview), choice);
    }

    [TestMethod]
    public void NothingKnownUsesApplicationTheme()
    {
        Assert.AreEqual(
            new ThemeChoice(_Light, ThemeChoiceSource.ApplicationRequestedTheme),
            ThemeResourceThemes.Decide([], new(null, null, _Light, _Dark))
        );
        Assert.AreEqual(new ThemeChoice(_Dark, ThemeChoiceSource.Application), ThemeResourceThemes.Decide([], new(null, null, null, _Dark)));
    }

    [TestMethod]
    public void ThemeDictionaryKeysTryTheThemeThenDefault()
    {
        CollectionAssert.AreEqual(new[] { _Light, _Default }, ThemeResourceThemes.GetThemeDictionaryKeys(_Light).ToArray());
        CollectionAssert.AreEqual(new[] { _Dark, _Default }, ThemeResourceThemes.GetThemeDictionaryKeys(_Dark).ToArray());
        CollectionAssert.AreEqual(new[] { _Default }, ThemeResourceThemes.GetThemeDictionaryKeys(_Default).ToArray());
    }

    [TestMethod]
    [DataRow(_Light, "", _Light)]
    [DataRow(_Dark, "", _Dark)]
    [DataRow(_Default, "Light", _Dark)]
    [DataRow(_Default, "Dark", _Light)]
    [DataRow(_Default, "", null)]
    [DataRow(_Default, "Light,Dark", null)]
    [DataRow(ThemeResourceThemes.HighContrast, "Light", null)]
    [DataRow("Custom", "Light", null)]
    [DataRow(null, "Light", null)]
    public void DefaultThemeDictionaryStandsForTheThemesWithoutOwnDictionary(string? key, string siblings, string? theme)
    {
        // Default serves the theme that has no dictionary of its own; when it serves both, the preview decides.
        var otherKeys = new HashSet<string>(siblings.Split(',', StringSplitOptions.RemoveEmptyEntries));

        Assert.AreEqual(theme, ThemeResourceThemes.FromThemeDictionaryKey(key, otherKeys.Contains));
    }

    [TestMethod]
    public void ThemeDictionariesAreMatchedByIdentity()
    {
        var light = new FakeDictionary("Light");
        var fallback = new FakeDictionary("Default");
        var map = new Dictionary<object, object> { [_Light] = light, [_Default] = fallback };

        Assert.AreEqual(_Light, ThemeResourceThemes.FromThemeDictionaries(light, map));
        Assert.AreEqual(_Dark, ThemeResourceThemes.FromThemeDictionaries(fallback, map));
        Assert.IsNull(ThemeResourceThemes.FromThemeDictionaries(new FakeDictionary("Light"), map));
    }

    [TestMethod]
    public void TheThemeOfADictionaryComesFromTheMapOrFromTheDictionaryThatOwnsIt()
    {
        // The search path of Visual Studio: a theme dictionary, then the ThemeDictionaries map, then the dictionary that owns it.
        var owner = new FakeDictionary("Page.Resources");
        FakeDictionary light = owner.Theme(_Light);
        FakeDictionary fallback = owner.Theme(_Default);

        Assert.AreEqual(_Light, FromParent(light, owner));
        Assert.AreEqual(_Light, FromParent(light, owner.ThemeDictionaries));
        Assert.AreEqual(_Dark, FromParent(fallback, owner));
        Assert.AreEqual(_Dark, FromParent(fallback, owner.ThemeDictionaries));
    }

    [TestMethod]
    public void ADictionaryThatIsNotInTheMapHasNoTheme()
    {
        // Entries are matched by identity: a dictionary of the same name, and the entry of another owner, are no theme dictionaries.
        var owner = new FakeDictionary("Page.Resources");
        FakeDictionary light = owner.Theme(_Light);
        var otherOwner = new FakeDictionary("Other.Resources");

        Assert.IsNull(FromParent(new FakeDictionary("Page.Resources[Light]"), owner));
        Assert.IsNull(FromParent(new FakeDictionary("Page.Resources[Light]"), owner.ThemeDictionaries));
        Assert.IsNull(FromParent(light, otherOwner));
        Assert.IsNull(FromParent(light, otherOwner.ThemeDictionaries));
    }

    [TestMethod]
    public void AParentThatIsNeitherTheOwnerNorTheMapGivesNoTheme()
    {
        var owner = new FakeDictionary("Page.Resources");
        FakeDictionary light = owner.Theme(_Light);

        Assert.IsNull(FromParent(light, new FakeElement("Page")));
        Assert.IsNull(FromParent(light, new object()));
    }

    [TestMethod]
    public void OnlyADictionaryCanBeAThemeDictionary()
    {
        // The scope is stored in the map, but it is an element: only a dictionary can be an entry of ThemeDictionaries.
        var owner = new FakeDictionary("Page.Resources");
        var element = new FakeElement("Page");
        owner.ThemeDictionaries[_Light] = element;

        Assert.IsNull(FromParent(element, owner));
        Assert.IsNull(FromParent(element, owner.ThemeDictionaries));
    }

    [TestMethod]
    public void AParentThatIsADictionaryAndTheMapAtOnceIsProbedAsAMap()
    {
        // A WinUI ResourceDictionary is a dictionary and a map at once; the ThemeDictionaries map can be one too. Its own
        // ThemeDictionaries are empty, so the entry is found only by probing the parent itself as a map.
        var theme = new MapDictionary();
        var map = new MapDictionary { [_Light] = theme };

        Assert.AreEqual(_Light, ThemeResourceThemes.FromParent<MapDictionary>(theme, map, owner => owner.ThemeDictionaries));
    }

    [TestMethod]
    public void ADictionaryThatOwnsTheMapIsAskedForItsThemeDictionariesFirst()
    {
        var theme = new MapDictionary();
        var owner = new MapDictionary { ThemeDictionaries = { [_Dark] = theme } };

        Assert.AreEqual(_Dark, ThemeResourceThemes.FromParent<MapDictionary>(theme, owner, dictionary => dictionary.ThemeDictionaries));
    }

    [TestMethod]
    public void DescribePairsEachScopeWithItsParent()
    {
        // brush -> 'Light' dictionary -> ThemeDictionaries map -> page dictionary -> page, the search path of Visual Studio.
        var pageResources = new FakeDictionary("Page.Resources");
        FakeDictionary light = pageResources.Theme(_Light);
        var page = new FakeElement("Page", _Dark);
        object brush = new();
        var parents = new Dictionary<object, object>
        {
            [brush] = light,
            [light] = pageResources.ThemeDictionaries,
            [pageResources.ThemeDictionaries] = pageResources,
            [pageResources] = page,
        };

        List<ThemeScopeFacts> facts = ThemeResourceThemes.Describe(brush, parents.GetValueOrDefault, FakeScopes.DictionaryTheme, FakeScopes.RequestedTheme);

        CollectionAssert.AreEqual(
            new ThemeScopeFacts[] { new(null, null), new(_Light, null), new(null, null), new(null, null), new(null, _Dark) },
            facts
        );
        Assert.AreEqual(new ThemeChoice(_Light, ThemeChoiceSource.ThemeDictionary), ThemeResourceThemes.Decide(facts, new(_Dark, _Dark, null, _Dark)));
    }

    [TestMethod]
    public void TitleBarLogoFollowsThePreviewAndAliasesInsideThemeDictionariesFollowTheirDictionary()
    {
        // TitleBar.xaml: the page dictionaries 'Light' and 'Dark' hold LogoSource, which the page reads through a
        // {ThemeResource}, and brushes that refer to brushes of XamlControlsResources.
        var controls = new FakeDictionary("XamlControlsResources");
        controls.Theme(_Default).Own["SubtleFillColorSecondaryBrush"] = "subtle dark";
        controls.Theme(_Light).Own["SubtleFillColorSecondaryBrush"] = "subtle light";
        var application = new FakeDictionary("Application.Resources");
        application.Merge(controls);
        var pageResources = new FakeDictionary("TitleBar.Resources");
        pageResources.Theme(_Light).Own["LogoSource"] = "logo light";
        pageResources.Theme(_Dark).Own["LogoSource"] = "logo dark";
        var page = new FakeElement("TitleBar") { Resources = pageResources };
        var image = new FakeElement("Image");
        object alias = new();
        var parents = new Dictionary<object, object>
        {
            [image] = page,
            [alias] = pageResources.Theme(_Light),
            [pageResources.Theme(_Light)] = pageResources.ThemeDictionaries,
            [pageResources.ThemeDictionaries] = pageResources,
            [pageResources] = page,
        };
        var preview = new ThemePreview(_Dark, _Dark, null, _Light);

        Assert.AreEqual("logo dark", Evaluate(image, "LogoSource"));
        Assert.AreEqual("subtle light", Evaluate(alias, "SubtleFillColorSecondaryBrush"));

        object? Evaluate(object owner, string key)
        {
            List<ThemeScopeFacts> walk = ThemeResourceThemes.Describe(owner, parents.GetValueOrDefault, FakeScopes.DictionaryTheme, FakeScopes.RequestedTheme);
            string theme = ThemeResourceThemes.Decide(walk, preview).Theme;
            var scopes = ResourceScopeWalk.Dictionaries(owner, parents.GetValueOrDefault, FakeScopes.DictionaryOf, [application]).ToList();
            return ThemeResourceLookup.Resolve(scopes, key, theme, new FakeDictionaries(true), application).Value;
        }
    }

    [TestMethod]
    [DataRow("RequestedTheme:Microsoft.UI.Xaml.FrameworkElement, Microsoft.WinUI", true)]
    [DataRow("RequestedTheme", true)]
    [DataRow(" RequestedTheme :Microsoft.UI.Xaml.FrameworkElement", true)]
    [DataRow("Background:Microsoft.UI.Xaml.Controls.Control, Microsoft.WinUI", false)]
    [DataRow("RequestedThemeOverride:Some.Type", false)]
    [DataRow(null, false)]
    public void RequestedThemeIsRecognizedWhateverItsDeclaringType(string? fullPropertyName, bool isRequestedTheme)
    {
        Assert.AreEqual(isRequestedTheme, ThemeResourceThemes.IsRequestedThemeProperty(fullPropertyName));
    }

    private static string? FromParent(object scope, object parent)
    {
        return ThemeResourceThemes.FromParent<FakeDictionary>(scope, parent, owner => owner.ThemeDictionaries);
    }

    /// <summary>A dictionary that is also a map, as WinUI's <c>ResourceDictionary</c> is, with <c>ThemeDictionaries</c> of its own.</summary>
    private sealed class MapDictionary : Dictionary<object, object>
    {
        public Dictionary<object, object> ThemeDictionaries { get; } = [];
    }
}
