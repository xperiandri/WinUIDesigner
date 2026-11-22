// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using WinUIDesigner.Surface.Common;

/// <summary>The properties set from a <c>{ThemeResource}</c>, evaluated again when the theme of their owner changes.</summary>
[TestClass]
public sealed class ThemeResourceRegistryTests
{
    private const string Background = "Background:Microsoft.UI.Xaml.Controls.Border, Microsoft.WinUI";
    private const string BorderBrush = "BorderBrush:Microsoft.UI.Xaml.Controls.Border, Microsoft.WinUI";
    private const string Light = ThemeResourceThemes.Light;
    private const string Dark = ThemeResourceThemes.Dark;

    [TestMethod]
    public void SecondSetOfSamePropertyReplacesEntry()
    {
        var registry = new ThemeResourceRegistry();
        object border = new();

        registry.Record(border, Background, "CardBackgroundFillColorDefaultBrush", Light);
        registry.Record(border, Background, "SystemFillColorCriticalBrush", Dark);
        registry.Record(border, BorderBrush, "CardStrokeColorDefaultBrush", Light);

        Assert.HasCount(2, registry.Entries);
        Assert.Contains(new ThemeResourceEntry(border, Background, "SystemFillColorCriticalBrush", Dark), registry.Entries);
    }

    [TestMethod]
    public void ClearForgetsEntry()
    {
        var registry = new ThemeResourceRegistry();
        object border = new();
        registry.Record(border, Background, "CardBackgroundFillColorDefaultBrush", Light);
        registry.Record(border, BorderBrush, "CardStrokeColorDefaultBrush", Light);

        Assert.IsTrue(registry.Forget(border, Background));
        Assert.IsFalse(registry.Forget(border, Background), "Forgotten once.");

        ThemeResourceEntry remaining = Assert.ContainsSingle(registry.Entries);
        Assert.AreEqual(BorderBrush, remaining.Property);
    }

    [TestMethod]
    public void EqualOwnersAreDistinctEntries()
    {
        // Owners compare by reference: two equal objects are two owners.
        var registry = new ThemeResourceRegistry();
        registry.Record("same text", Background, "Key", Light);
        registry.Record(new string("same text".AsSpan()), Background, "Key", Light);

        Assert.HasCount(2, registry.Entries);
    }

    [TestMethod]
    public void ReleasedOwnersAreForgotten()
    {
        var registry = new ThemeResourceRegistry();
        object released = new();
        object kept = new();
        registry.Record(released, Background, "Key", Light);
        registry.Record(released, BorderBrush, "Key", Light);
        registry.Record(kept, Background, "Key", Light);

        registry.ForgetOwners(new HashSet<object>(ReferenceEqualityComparer.Instance) { released });

        ThemeResourceEntry remaining = Assert.ContainsSingle(registry.Entries);
        Assert.AreSame(kept, remaining.Owner);
    }

    [TestMethod]
    public void ReapplyVisitsEachLiveEntryOnce()
    {
        // Entries of a page and of a shared application dictionary, which has no surface: each is visited once a pass.
        var registry = new ThemeResourceRegistry();
        object pageBorder = new();
        object applicationSetter = new();
        registry.Record(pageBorder, Background, "Key", Light);
        registry.Record(applicationSetter, "Value:Microsoft.UI.Xaml.Setter, Microsoft.WinUI", "Key", Light);
        var decided = new List<object>();
        var applied = new List<object>();

        ThemeResourceReapplyResult result = registry.Reapply(
            entry =>
            {
                decided.Add(entry.Owner);
                return Dark;
            },
            (entry, _) =>
            {
                applied.Add(entry.Owner);
                return true;
            }
        );

        Assert.AreEqual(new ThemeResourceReapplyResult(2, 2, 0), result);
        CollectionAssert.AreEquivalent(new[] { pageBorder, applicationSetter }, decided);
        CollectionAssert.AreEquivalent(new[] { pageBorder, applicationSetter }, applied);
        Assert.DoesNotContain(entry => entry.Theme != Dark, registry.Entries);
    }

    [TestMethod]
    public void ReapplySkipsEntriesAlreadyResolvedForTheTheme()
    {
        // Visual Studio sends the same artboard colours again after every new surface: nothing is set again.
        var registry = new ThemeResourceRegistry();
        registry.Record(new object(), Background, "Key", Dark);
        int applied = 0;

        ThemeResourceReapplyResult result = registry.Reapply(_ => Dark, (_, _) => ++applied > 0);

        Assert.AreEqual(new ThemeResourceReapplyResult(1, 0, 0), result);
        Assert.AreEqual(0, applied);
    }

    [TestMethod]
    public void AnEntryThatCouldNotBeAppliedKeepsItsThemeAndIsTriedAgain()
    {
        // The Setter of a style in use is sealed: the value stays the one of the old theme.
        var registry = new ThemeResourceRegistry();
        registry.Record(new object(), "Value:Microsoft.UI.Xaml.Setter, Microsoft.WinUI", "Key", Light);

        Assert.AreEqual(new ThemeResourceReapplyResult(1, 0, 1), registry.Reapply(_ => Dark, (_, _) => false));
        Assert.AreEqual(Light, Assert.ContainsSingle(registry.Entries).Theme);
        Assert.AreEqual(new ThemeResourceReapplyResult(1, 1, 0), registry.Reapply(_ => Dark, (_, _) => true));
    }

    [TestMethod]
    public void RequestedThemeEditOnAncestorRevisitsDescendants()
    {
        // A grid gets RequestedTheme="Dark": the border inside it moves to the dark theme, the one beside it does not.
        var page = new FakeElement("Page");
        var grid = new FakeElement("Grid");
        var inside = new FakeElement("Inside");
        var beside = new FakeElement("Beside");
        var parents = new Dictionary<object, object> { [grid] = page, [inside] = grid, [beside] = page };
        var preview = new ThemePreview(Light, Light, null, Light);
        string Decide(ThemeResourceEntry entry) => ThemeResourceThemes.Decide(
            ThemeResourceThemes.Describe(entry.Owner, parents.GetValueOrDefault, ThemeResourceThemesTests.DictionaryTheme, ThemeResourceThemesTests.RequestedTheme),
            preview
        ).Theme;
        var registry = new ThemeResourceRegistry();
        registry.Record(inside, Background, "Key", Light);
        registry.Record(beside, Background, "Key", Light);
        var applied = new List<object>();

        grid.RequestedTheme = Dark;
        registry.Reapply(Decide, (entry, _) =>
        {
            applied.Add(entry.Owner);
            return true;
        });

        CollectionAssert.AreEqual(new object[] { inside }, applied);
    }
}
