// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using WinUIDesigner.Surface.Common;

[TestClass]
public sealed class ContentDialogThemePolicyTests
{
    private const string Light = ContentDialogThemePolicy.Light;
    private const string Dark = ContentDialogThemePolicy.Dark;

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void TheWhiteMaterialCardOfALightApplicationTakesTheDarkSurfaceInADarkPreview(bool referencesFirst)
    {
        var resources = new ThemeResources(referencesFirst);

        BrushChoice choice = ContentDialogThemePolicy.Choose(resources.LightSurface, false, Dark, Light, resources.All);

        Assert.AreSame(resources.DarkSurface.Brush, choice.Brush?.Brush, choice.Reason);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void TheMaterialTextOfALightApplicationTakesTheDarkOnSurfaceVariantInADarkPreview(bool referencesFirst)
    {
        var resources = new ThemeResources(referencesFirst);

        BrushChoice choice = ContentDialogThemePolicy.Choose(resources.LightOnSurfaceVariant, false, Dark, Light, resources.All);

        Assert.AreSame(resources.DarkOnSurfaceVariant.Brush, choice.Brush?.Brush, choice.Reason);
    }

    [TestMethod]
    public void ABorderThatBothThemesDrawAlikeIsKept()
    {
        // The border of the Material dialog was the dark stroke of WinUI, #66757575, which the light theme has too.
        var resources = new ThemeResources(referencesFirst: true);

        BrushChoice choice = ContentDialogThemePolicy.Choose(resources.DarkStroke, false, Dark, Light, resources.All);

        Assert.IsNull(choice.Brush, choice.Reason);
    }

    [TestMethod]
    public void ALightPreviewOfALightApplicationKeepsTheBrushesOfTheStyle()
    {
        // Before: the object of the light dictionary was put aside, and its colour found TextFillColorInverseBrush, white text.
        var resources = new ThemeResources(referencesFirst: true);

        BrushChoice choice = ContentDialogThemePolicy.Choose(resources.LightTextPrimary, false, Light, Light, resources.All);

        Assert.IsNull(choice.Brush, choice.Reason);
    }

    [TestMethod]
    public void ADarkPreviewOfADarkApplicationKeepsTheBrushesOfTheStyle()
    {
        // Before: the white text of the dark theme found a white key of the light theme and took its dark brush.
        var resources = new ThemeResources(referencesFirst: true);

        BrushChoice choice = ContentDialogThemePolicy.Choose(resources.DarkTextPrimary, false, Dark, Dark, resources.All);

        Assert.IsNull(choice.Brush, choice.Reason);
    }

    [TestMethod]
    [DataRow(Dark, Light)]
    [DataRow(Light, Dark)]
    public void ABrushOfThePreviewThemeIsKeptWhenTheApplicationHasTheOtherTheme(string previewTheme, string applicationTheme)
    {
        // The style of WinUI follows the theme of the dialog, so its brushes are already those of the preview.
        var resources = new ThemeResources(referencesFirst: true);
        ThemeBrush current = previewTheme == Dark ? resources.DarkTextPrimary : resources.LightTextPrimary;

        BrushChoice choice = ContentDialogThemePolicy.Choose(current, false, previewTheme, applicationTheme, resources.All);

        Assert.IsNull(choice.Brush, choice.Reason);
    }

    [TestMethod]
    public void ABrushOfTheApplicationThemeTakesTheBrushOfThePreviewThemeUnderItsKey()
    {
        var resources = new ThemeResources(referencesFirst: true);

        BrushChoice choice = ContentDialogThemePolicy.Choose(resources.LightTextPrimary, false, Dark, Light, resources.All);

        Assert.AreSame(resources.DarkTextPrimary.Brush, choice.Brush?.Brush, choice.Reason);
    }

    [TestMethod]
    public void HighContrastIsNeverTheThemeThatABrushComesFrom()
    {
        // Before: the black of SurfaceStrokeColorFlyoutBrush in HighContrast gave its dark brush, #33000000, to black text.
        var resources = new ThemeResources(referencesFirst: true);
        ThemeBrush black = Solid(0xFF000000);
        ThemeDictionarySet references = Set(("Default", "DialogText", black), ("Light", "DialogText", black));

        BrushChoice choice = ContentDialogThemePolicy.Choose(black, false, Dark, Light, [resources.WinUI, references]);

        Assert.IsNull(choice.Brush, choice.Reason);
    }

    [TestMethod]
    public void ABrushThatNoThemeDictionaryHasIsKept()
    {
        // A colour that the style or the document gives, as Background="White": it is white in the running app in either theme.
        var resources = new ThemeResources(referencesFirst: true);

        BrushChoice choice = ContentDialogThemePolicy.Choose(Solid(0xFFFFFFFF), false, Dark, Light, resources.All);

        Assert.IsNull(choice.Brush, choice.Reason);
    }

    [TestMethod]
    public void ABrushThatTheDocumentSetsOnTheDialogIsKept()
    {
        // Also a brush of the theme of the application, as {StaticResource SurfaceBrush} on the dialog.
        var resources = new ThemeResources(referencesFirst: true);

        BrushChoice choice = ContentDialogThemePolicy.Choose(resources.LightSurface, true, Dark, Light, resources.All);

        Assert.IsNull(choice.Brush, choice.Reason);
    }

    [TestMethod]
    public void TheDarkMaterialCardOfADarkApplicationTakesTheLightSurfaceInALightPreview()
    {
        var resources = new ThemeResources(referencesFirst: true);
        ThemeDictionarySet references = Set(("Default", "ContentDialogBackground", resources.DarkSurface), ("Light", "ContentDialogBackground", resources.DarkSurface));

        BrushChoice choice = ContentDialogThemePolicy.Choose(resources.DarkSurface, false, Light, Dark, [resources.WinUI, references, resources.Palette]);

        Assert.AreSame(resources.LightSurface.Brush, choice.Brush?.Brush, choice.Reason);
    }

    [TestMethod]
    public void ABrushThatOnlyThePreviewThemeHasIsKept()
    {
        // Only the dark dictionary has ContentDialogBackground, so the light theme of the application did not give the brush to
        // the style: it is a brush of the preview. Before, it was matched by its colour (the one of the light SurfaceBrush) and
        // became the dark SurfaceBrush.
        var resources = new ThemeResources(referencesFirst: true);
        ThemeBrush white = Solid(0xFFFFFFFF);
        ThemeDictionarySet previewOnly = Set(("Default", "ContentDialogBackground", white), ("Light", "Unrelated", resources.LightBase));

        BrushChoice choice = ContentDialogThemePolicy.Choose(white, false, Dark, Light, [resources.WinUI, previewOnly, resources.Palette]);

        Assert.IsNull(choice.Brush, choice.Reason);
    }

    [TestMethod]
    public void AReferenceThatOnlyTheApplicationThemeHasIsMatchedByItsColour()
    {
        // A reference resolved for the light theme of the application under the light key only, to a white brush that is not
        // the object of the palette: its colour gives SurfaceBrush.
        var resources = new ThemeResources(referencesFirst: true);
        ThemeBrush white = Solid(0xFFFFFFFF);
        ThemeDictionarySet applicationOnly = Set(("Light", "ContentDialogBackground", white), ("Default", "Unrelated", resources.DarkBase));

        BrushChoice choice = ContentDialogThemePolicy.Choose(white, false, Dark, Light, [resources.WinUI, applicationOnly, resources.Palette]);

        Assert.AreSame(resources.DarkSurface.Brush, choice.Brush?.Brush, choice.Reason);
    }

    [TestMethod]
    [DataRow(null, Light, false)]
    [DataRow(Light, Light, false)]
    [DataRow(Dark, Dark, false)]
    [DataRow(Dark, Light, true)]
    [DataRow(Light, Dark, true)]
    public void OnlyAPreviewWithAnotherThemeThanTheApplicationNeedsOtherBrushes(string? previewTheme, string applicationTheme, bool differs)
    {
        Assert.AreEqual(differs, ContentDialogThemePolicy.PreviewThemeDiffers(previewTheme, applicationTheme));
    }

    [TestMethod]
    public void OnlyTheRectangleThatTheFrameworkOpensWithTheDialogIsTheSmokeLayer()
    {
        Assert.IsTrue(ContentDialogThemePolicy.IsSmokeLayer(childIsRectangle: true, popupIsInDialog: false));

        // Before: a <Popup> of the document with a Rectangle in it was given the colour of the smoke too.
        Assert.IsFalse(ContentDialogThemePolicy.IsSmokeLayer(childIsRectangle: true, popupIsInDialog: true));

        // The popup that has the dialog in it.
        Assert.IsFalse(ContentDialogThemePolicy.IsSmokeLayer(childIsRectangle: false, popupIsInDialog: false));
    }

    [TestMethod]
    public void WithoutABrushOfTheResourcesTheSmokeHasTheColourOfTheFramework()
    {
        // SystemAltMediumColor of the dark (Default) and the light theme of WinUI.
        Assert.AreEqual(0x99000000u, ContentDialogThemePolicy.SmokeColor(Dark));
        Assert.AreEqual(0x99FFFFFFu, ContentDialogThemePolicy.SmokeColor(Light));
    }

    [TestMethod]
    public void TheNameOfASurfaceComesFirstThenTheShortestName()
    {
        Assert.AreEqual("SurfaceBrush", ContentDialogThemePolicy.PreferredKey(["InfoBadgeForeground", "SurfaceVariantBrush", "SurfaceBrush", "Hub"]));
        Assert.AreEqual("SurfaceVariantBrush", ContentDialogThemePolicy.PreferredKey(["InfoBadgeForeground", "SurfaceVariantBrush", "Hub"]));
        Assert.AreEqual("Hub", ContentDialogThemePolicy.PreferredKey(["InfoBadgeForeground", "Hub", "TextFillColorInverseBrush"]));
        Assert.AreEqual("Bar", ContentDialogThemePolicy.PreferredKey(["Foo", "Bar"]));
        Assert.IsNull(ContentDialogThemePolicy.PreferredKey([]));
    }

    internal static ThemeBrush Solid(uint color, double opacity = 1) => new(new object(), color, opacity);

    internal static ThemeDictionarySet Set(params (string Theme, string Key, ThemeBrush Brush)[] entries)
    {
        Dictionary<string, Dictionary<string, ThemeBrush>> themes = new(StringComparer.Ordinal);
        foreach ((string theme, string key, ThemeBrush brush) in entries)
        {
            if (!themes.TryGetValue(theme, out Dictionary<string, ThemeBrush>? brushes))
            {
                themes[theme] = brushes = new(StringComparer.Ordinal);
            }

            brushes[key] = brush;
        }

        return new ThemeDictionarySet(themes.ToDictionary(pair => pair.Key, pair => (IReadOnlyDictionary<string, ThemeBrush>)pair.Value, StringComparer.Ordinal));
    }

    /// <summary>
    /// Theme dictionaries like the ones that the designer found in an Uno Material solution: part of the resources of WinUI
    /// (<c>XamlControlsResources</c>, whose dark theme is <c>Default</c>), the palette of the app, and the resources of the
    /// dialog, references to the palette that were resolved for the theme of the application (light), so that both themes
    /// have the light objects of the palette under them.
    /// </summary>
    internal sealed class ThemeResources
    {
        public readonly ThemeBrush DarkTextPrimary = Solid(0xFFFFFFFF);
        public readonly ThemeBrush LightTextPrimary = Solid(0xE4000000);
        public readonly ThemeBrush HighContrastTextPrimary = Solid(0xFF000000);
        public readonly ThemeBrush DarkTextInverse = Solid(0xE4000000);
        public readonly ThemeBrush LightTextInverse = Solid(0xFFFFFFFF);
        public readonly ThemeBrush DarkTextOnAccent = Solid(0xFF000000);
        public readonly ThemeBrush LightTextOnAccent = Solid(0xFFFFFFFF);
        public readonly ThemeBrush DarkStroke = Solid(0x66757575);
        public readonly ThemeBrush LightStroke = Solid(0x66757575);
        public readonly ThemeBrush DarkFlyoutStroke = Solid(0x33000000);
        public readonly ThemeBrush LightFlyoutStroke = Solid(0x0F000000);
        public readonly ThemeBrush HighContrastFlyoutStroke = Solid(0xFF000000);
        public readonly ThemeBrush DarkBase = Solid(0xFF202020);
        public readonly ThemeBrush LightBase = Solid(0xFFF3F3F3);

        public readonly ThemeBrush DarkSurface = Solid(0xFF302D37);
        public readonly ThemeBrush LightSurface = Solid(0xFFFFFFFF);
        public readonly ThemeBrush DarkOnSurfaceVariant = Solid(0xFFC9C5D0);
        public readonly ThemeBrush LightOnSurfaceVariant = Solid(0xFF8B8494);

        public ThemeResources(bool referencesFirst)
        {
            WinUI = Set(
                ("Default", "TextFillColorPrimaryBrush", DarkTextPrimary),
                ("Default", "TextFillColorInverseBrush", DarkTextInverse),
                ("Default", "TextOnAccentFillColorPrimaryBrush", DarkTextOnAccent),
                ("Default", "InfoBadgeForeground", DarkTextOnAccent),
                ("Default", "SurfaceStrokeColorDefaultBrush", DarkStroke),
                ("Default", "SurfaceStrokeColorFlyoutBrush", DarkFlyoutStroke),
                ("Default", "SolidBackgroundFillColorBaseBrush", DarkBase),
                ("Default", "ContentDialogForeground", DarkTextPrimary),
                ("Default", "ContentDialogBackground", DarkBase),
                ("Default", "ContentDialogBorderBrush", DarkStroke),
                ("HighContrast", "TextFillColorPrimaryBrush", HighContrastTextPrimary),
                ("HighContrast", "SurfaceStrokeColorFlyoutBrush", HighContrastFlyoutStroke),
                ("HighContrast", "ContentDialogForeground", HighContrastTextPrimary),
                ("Light", "TextFillColorPrimaryBrush", LightTextPrimary),
                ("Light", "TextFillColorInverseBrush", LightTextInverse),
                ("Light", "TextOnAccentFillColorPrimaryBrush", LightTextOnAccent),
                ("Light", "InfoBadgeForeground", LightTextOnAccent),
                ("Light", "SurfaceStrokeColorDefaultBrush", LightStroke),
                ("Light", "SurfaceStrokeColorFlyoutBrush", LightFlyoutStroke),
                ("Light", "SolidBackgroundFillColorBaseBrush", LightBase),
                ("Light", "ContentDialogForeground", LightTextPrimary),
                ("Light", "ContentDialogBackground", LightBase),
                ("Light", "ContentDialogBorderBrush", LightStroke)
            );

            Palette = Set(
                ("Default", "SurfaceBrush", DarkSurface),
                ("Default", "OnSurfaceVariantBrush", DarkOnSurfaceVariant),
                ("Light", "SurfaceBrush", LightSurface),
                ("Light", "OnSurfaceVariantBrush", LightOnSurfaceVariant)
            );

            DialogReferences = Set(
                ("Default", "ContentDialogBackground", LightSurface),
                ("Default", "ContentDialogForeground", LightOnSurfaceVariant),
                ("Light", "ContentDialogBackground", LightSurface),
                ("Light", "ContentDialogForeground", LightOnSurfaceVariant)
            );

            All = referencesFirst ? [WinUI, DialogReferences, Palette] : [WinUI, Palette, DialogReferences];
        }

        public ThemeDictionarySet WinUI { get; }

        public ThemeDictionarySet Palette { get; }

        public ThemeDictionarySet DialogReferences { get; }

        public IReadOnlyList<ThemeDictionarySet> All { get; }

        /// <summary>The brush of the dictionaries whose object is <paramref name="value"/>, as the surface describes a brush.</summary>
        public ThemeBrush? Describe(object value)
        {
            return All
                .SelectMany(set => set.Themes.Values)
                .SelectMany(theme => theme.Values)
                .Where(brush => ReferenceEquals(brush.Brush, value))
                .Select(brush => (ThemeBrush?)brush)
                .FirstOrDefault();
        }
    }
}
