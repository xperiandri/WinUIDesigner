// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using WinUIDesigner.Surface.Common;

/// <summary>The theme that a <c>{ThemeResource}</c> is evaluated in.</summary>
[TestClass]
public sealed class ThemeResourceThemesTests
{
    private const string Light = ThemeResourceThemes.Light;
    private const string Dark = ThemeResourceThemes.Dark;
    private const string Default = ThemeResourceThemes.Default;

    [TestMethod]
    public void ClosestExplicitRequestedThemeWins()
    {
        ThemeChoice choice = ThemeResourceThemes.Decide([new(null, null), new(null, Dark), new(null, Light)], new(Light, Light, null, Light));

        Assert.AreEqual(new ThemeChoice(Dark, ThemeChoiceSource.RequestedTheme), choice);
    }

    [TestMethod]
    public void ReferenceInsideThemeDictionaryUsesItsKey()
    {
        // The theme dictionary is farther from the reference than an element that requests the dark theme, and it is
        // used only while its theme is shown.
        ThemeChoice choice = ThemeResourceThemes.Decide([new(null, Dark), new(Light, null), new(null, Dark)], new(Dark, Dark, null, Dark));

        Assert.AreEqual(new ThemeChoice(Light, ThemeChoiceSource.ThemeDictionary), choice);
    }

    [TestMethod]
    public void DefaultUsesPreviewTheme()
    {
        ThemeChoice choice = ThemeResourceThemes.Decide([new(null, null), new(null, null)], new(Dark, Light, Light, Light));

        Assert.AreEqual(new ThemeChoice(Dark, ThemeChoiceSource.Surface), choice);
    }

    [TestMethod]
    public void DefaultPreviewUsesLastArtboardTheme()
    {
        // A document that is being built has no surface yet; the artboard colours of another document say the theme.
        ThemeChoice choice = ThemeResourceThemes.Decide([new(null, null)], new(null, Dark, Light, Light));

        Assert.AreEqual(new ThemeChoice(Dark, ThemeChoiceSource.LastPreview), choice);
    }

    [TestMethod]
    public void NothingKnownUsesApplicationTheme()
    {
        Assert.AreEqual(
            new ThemeChoice(Light, ThemeChoiceSource.ApplicationRequestedTheme),
            ThemeResourceThemes.Decide([], new(null, null, Light, Dark))
        );
        Assert.AreEqual(new ThemeChoice(Dark, ThemeChoiceSource.Application), ThemeResourceThemes.Decide([], new(null, null, null, Dark)));
    }

    [TestMethod]
    public void ThemeDictionaryKeysTryTheThemeThenDefault()
    {
        CollectionAssert.AreEqual(new[] { Light, Default }, ThemeResourceThemes.GetThemeDictionaryKeys(Light).ToArray());
        CollectionAssert.AreEqual(new[] { Dark, Default }, ThemeResourceThemes.GetThemeDictionaryKeys(Dark).ToArray());
        CollectionAssert.AreEqual(new[] { Default }, ThemeResourceThemes.GetThemeDictionaryKeys(Default).ToArray());
    }

    [TestMethod]
    [DataRow(Light, "", Light)]
    [DataRow(Dark, "", Dark)]
    [DataRow(Default, "Light", Dark)]
    [DataRow(Default, "Dark", Light)]
    [DataRow(Default, "", null)]
    [DataRow(Default, "Light,Dark", null)]
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
        var map = new Dictionary<object, object> { [Light] = light, [Default] = fallback };

        Assert.AreEqual(Light, ThemeResourceThemes.FromThemeDictionaries(light, map));
        Assert.AreEqual(Dark, ThemeResourceThemes.FromThemeDictionaries(fallback, map));
        Assert.IsNull(ThemeResourceThemes.FromThemeDictionaries(new FakeDictionary("Light"), map));
    }

    [TestMethod]
    public void DescribePairsEachScopeWithItsParent()
    {
        // brush -> 'Light' dictionary -> ThemeDictionaries map -> page dictionary -> page, the search path of Visual Studio.
        var pageResources = new FakeDictionary("Page.Resources");
        FakeDictionary light = pageResources.Theme(Light);
        var page = new FakeElement("Page", Dark);
        object brush = new();
        var parents = new Dictionary<object, object>
        {
            [brush] = light,
            [light] = pageResources.ThemeDictionaries,
            [pageResources.ThemeDictionaries] = pageResources,
            [pageResources] = page,
        };

        List<ThemeScopeFacts> facts = ThemeResourceThemes.Describe(brush, parents.GetValueOrDefault, DictionaryTheme, RequestedTheme);

        CollectionAssert.AreEqual(
            new ThemeScopeFacts[] { new(null, null), new(Light, null), new(null, null), new(null, null), new(null, Dark) },
            facts
        );
        Assert.AreEqual(new ThemeChoice(Light, ThemeChoiceSource.ThemeDictionary), ThemeResourceThemes.Decide(facts, new(Dark, Dark, null, Dark)));
    }

    [TestMethod]
    public void TitleBarLogoFollowsThePreviewAndAliasesInsideThemeDictionariesFollowTheirDictionary()
    {
        // TitleBar.xaml: the page dictionaries 'Light' and 'Dark' hold LogoSource, which the page reads through a
        // {ThemeResource}, and brushes that refer to brushes of XamlControlsResources.
        var controls = new FakeDictionary("XamlControlsResources");
        controls.Theme(Default).Own["SubtleFillColorSecondaryBrush"] = "subtle dark";
        controls.Theme(Light).Own["SubtleFillColorSecondaryBrush"] = "subtle light";
        var application = new FakeDictionary("Application.Resources");
        application.Merge(controls);
        var pageResources = new FakeDictionary("TitleBar.Resources");
        pageResources.Theme(Light).Own["LogoSource"] = "logo light";
        pageResources.Theme(Dark).Own["LogoSource"] = "logo dark";
        var page = new FakeElement("TitleBar") { Resources = pageResources };
        var image = new FakeElement("Image");
        object alias = new();
        var parents = new Dictionary<object, object>
        {
            [image] = page,
            [alias] = pageResources.Theme(Light),
            [pageResources.Theme(Light)] = pageResources.ThemeDictionaries,
            [pageResources.ThemeDictionaries] = pageResources,
            [pageResources] = page,
        };
        var preview = new ThemePreview(Dark, Dark, null, Light);

        Assert.AreEqual("logo dark", Evaluate(image, "LogoSource"));
        Assert.AreEqual("subtle light", Evaluate(alias, "SubtleFillColorSecondaryBrush"));

        object? Evaluate(object owner, string key)
        {
            List<ThemeScopeFacts> walk = ThemeResourceThemes.Describe(owner, parents.GetValueOrDefault, DictionaryTheme, RequestedTheme);
            string theme = ThemeResourceThemes.Decide(walk, preview).Theme;
            var scopes = ResourceScopeWalk.Dictionaries(owner, parents.GetValueOrDefault, DictionaryOf, [application]).ToList();
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

    // As the surface does: the parent is the ThemeDictionaries map, or the dictionary that owns it.
    internal static string? DictionaryTheme(object scope, object parent) => parent switch
    {
        FakeDictionary owner when ThemeResourceThemes.FromThemeDictionaries(scope, owner.ThemeDictionaries) is { } theme => theme,
        IDictionary<object, object> map => ThemeResourceThemes.FromThemeDictionaries(scope, map),
        _ => null,
    };

    internal static string? RequestedTheme(object scope) => (scope as FakeElement)?.RequestedTheme;

    internal static FakeDictionary? DictionaryOf(object scope) => scope switch
    {
        FakeElement element => element.Resources,
        FakeDictionary dictionary => dictionary,
        _ => null,
    };
}

/// <summary>An element of a fake tree: a theme it may request, and resources.</summary>
internal sealed class FakeElement(string name, string? requestedTheme = null)
{
    public string Name { get; } = name;

    public string? RequestedTheme { get; set; } = requestedTheme;

    public FakeDictionary Resources { get; init; } = new($"{name}.Resources");

    public override string ToString() => Name;
}
