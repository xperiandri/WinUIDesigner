// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using WinUIDesigner.Surface.Common;
using static ContentDialogThemePolicyTests;

[TestClass]
public sealed class ContentDialogBrushRetargeterTests
{
    private const string Light = ContentDialogThemePolicy.Light;
    private const string Dark = ContentDialogThemePolicy.Dark;

    [TestMethod]
    public void TheBrushesOfTheStyleTakeTheOnesOfThePreviewTheme()
    {
        var (resources, dialog, retargeter) = MaterialDialog();

        Refresh(retargeter, dialog, resources, Dark, Light);

        Assert.AreSame(resources.DarkSurface.Brush, dialog.Local["Background"]);
        Assert.AreSame(resources.DarkOnSurfaceVariant.Brush, dialog.Local["Foreground"]);
        Assert.DoesNotContain("BorderBrush", dialog.Local.Keys);
    }

    [TestMethod]
    public void TheBrushesOfTheStyleComeBackWhenThePreviewGetsTheThemeOfTheApplication()
    {
        var (resources, dialog, retargeter) = MaterialDialog();
        Refresh(retargeter, dialog, resources, Dark, Light);

        Refresh(retargeter, dialog, resources, Light, Light);

        Assert.IsEmpty(dialog.Local);
        Assert.AreSame(resources.LightSurface.Brush, dialog.GetValue("Background"));
    }

    [TestMethod]
    public void AValueThatTheDocumentSetsOverAWrittenBrushStays()
    {
        var (resources, dialog, retargeter) = MaterialDialog();
        Refresh(retargeter, dialog, resources, Dark, Light);
        object red = Solid(0xFFFF0000).Brush;
        dialog.Local["Background"] = red;

        Refresh(retargeter, dialog, resources, Light, Light);
        Assert.AreSame(red, dialog.Local["Background"]);

        Refresh(retargeter, dialog, resources, Dark, Light);
        Assert.AreSame(red, dialog.Local["Background"]);
    }

    [TestMethod]
    public void ABrushThatTheDocumentSetsIsNotReplaced()
    {
        // {StaticResource SurfaceBrush} on the dialog: the light object of the palette, set by the document.
        var (resources, dialog, retargeter) = MaterialDialog();
        dialog.Local["Background"] = resources.LightSurface.Brush;

        Refresh(retargeter, dialog, resources, Dark, Light);

        Assert.AreSame(resources.LightSurface.Brush, dialog.Local["Background"]);
    }

    [TestMethod]
    public void TheResourcesAreReadOnlyWhereABrushNeedsThem()
    {
        var (resources, dialog, retargeter) = MaterialDialog();

        int collected = Refresh(retargeter, dialog, resources, Light, Light);
        Assert.AreEqual(0, collected);

        collected = Refresh(retargeter, dialog, resources, Dark, Light);
        Assert.AreEqual(1, collected);
    }

    [TestMethod]
    public void WithoutEditsTheBrushesAreNotChosenAgainForTheSameThemes()
    {
        var (resources, dialog, retargeter) = MaterialDialog();
        Refresh(retargeter, dialog, resources, Dark, Light);
        dialog.Style["Background"] = resources.LightBase.Brush;

        int collected = Refresh(retargeter, dialog, resources, Dark, Light);

        Assert.AreEqual(0, collected);
        Assert.AreSame(resources.DarkSurface.Brush, dialog.Local["Background"]);
    }

    [TestMethod]
    public void AfterEditsTheBrushOfAnotherStyleIsChosen()
    {
        // The document gives the dialog another style while the brush that the surface wrote is still set on it.
        var (resources, dialog, retargeter) = MaterialDialog();
        Refresh(retargeter, dialog, resources, Dark, Light);
        dialog.Style["Background"] = resources.LightBase.Brush;

        Refresh(retargeter, dialog, resources, Dark, Light, afterEdits: true);

        Assert.AreSame(resources.DarkBase.Brush, dialog.Local["Background"]);
    }

    [TestMethod]
    public void AValueThatTheDocumentRemovesGivesTheChosenBrushOfTheStyleBack()
    {
        // Background="Red" is added to the dialog and removed again: the white card of the style must not come back.
        var (resources, dialog, retargeter) = MaterialDialog();
        Refresh(retargeter, dialog, resources, Dark, Light);
        object red = Solid(0xFFFF0000).Brush;
        dialog.Local["Background"] = red;
        Refresh(retargeter, dialog, resources, Dark, Light, afterEdits: true);
        Assert.AreSame(red, dialog.Local["Background"]);

        dialog.Local.Remove("Background");
        Refresh(retargeter, dialog, resources, Dark, Light, afterEdits: true);

        Assert.AreSame(resources.DarkSurface.Brush, dialog.Local["Background"]);
    }

    [TestMethod]
    public void EachBrushIsChosenOnceAsLongAsTheThemesStay()
    {
        var (resources, dialog, retargeter) = MaterialDialog();

        Assert.AreEqual(1, Refresh(retargeter, dialog, resources, Dark, Light));
        Assert.AreEqual(0, Refresh(retargeter, dialog, resources, Dark, Light, afterEdits: true));
        Assert.AreSame(resources.DarkSurface.Brush, dialog.Local["Background"]);

        Assert.AreEqual(0, Refresh(retargeter, dialog, resources, Dark, Dark));
        Assert.AreEqual(1, Refresh(retargeter, dialog, resources, Dark, Light));
    }

    [TestMethod]
    public void ForgettingTheDialogLeavesItsValuesAlone()
    {
        // The root was replaced: what the surface wrote on the old dialog is not taken from the new one.
        var (resources, dialog, retargeter) = MaterialDialog();
        Refresh(retargeter, dialog, resources, Dark, Light);
        retargeter.Forget();

        Refresh(retargeter, dialog, resources, Light, Light);

        Assert.AreSame(resources.DarkSurface.Brush, dialog.Local["Background"]);
    }

    /// <summary>The Material dialog of the Uno solution: the light brushes of the palette from its style, the dark stroke of WinUI.</summary>
    private static (ThemeResources Resources, FakeDialog Dialog, ContentDialogBrushRetargeter Retargeter) MaterialDialog()
    {
        var resources = new ThemeResources(referencesFirst: true);
        var dialog = new FakeDialog();
        dialog.Style["Background"] = resources.LightSurface.Brush;
        dialog.Style["BorderBrush"] = resources.DarkStroke.Brush;
        dialog.Style["Foreground"] = resources.LightOnSurfaceVariant.Brush;
        return (resources, dialog, new ContentDialogBrushRetargeter());
    }

    /// <summary>Chooses the brushes of the dialog and says how many times the resources were read.</summary>
    private static int Refresh(ContentDialogBrushRetargeter retargeter, FakeDialog dialog, ThemeResources resources, string? previewTheme, string applicationTheme, bool afterEdits = false)
    {
        int collected = 0;
        retargeter.Refresh(
            dialog,
            previewTheme,
            applicationTheme,
            afterEdits,
            () =>
            {
                collected++;
                return resources.All;
            },
            resources.Describe
        );

        return collected;
    }

    /// <summary>A dialog with the values that its style gives and the values that are set on it.</summary>
    private sealed class FakeDialog : IDialogBrushProperties
    {
        public Dictionary<string, object> Local { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, object> Style { get; } = new(StringComparer.Ordinal);

        public bool TryGetLocalValue(string property, out object? value)
        {
            bool found = Local.TryGetValue(property, out object? local);
            value = local;
            return found;
        }

        public object? GetValue(string property) => Local.TryGetValue(property, out object? local) ? local : Style.GetValueOrDefault(property);

        public void SetValue(string property, object value) => Local[property] = value;

        public void ClearValue(string property) => Local.Remove(property);
    }
}
