// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using WinUIDesigner.Surface.Common;

/// <summary>The properties set from a <c>{ThemeResource}</c>, evaluated again when the theme of their owner changes.</summary>
[TestClass]
public sealed class ThemeResourceRegistryTests
{
    private const string _Background = "Background:Microsoft.UI.Xaml.Controls.Border, Microsoft.WinUI";
    private const string _BorderBrush = "BorderBrush:Microsoft.UI.Xaml.Controls.Border, Microsoft.WinUI";
    private const string _Light = ThemeResourceThemes.Light;
    private const string _Dark = ThemeResourceThemes.Dark;

    [TestMethod]
    public void SecondSetOfSamePropertyReplacesEntry()
    {
        var registry = new ThemeResourceRegistry();
        object border = new();

        registry.Record(border, _Background, "CardBackgroundFillColorDefaultBrush", _Light);
        registry.Record(border, _Background, "SystemFillColorCriticalBrush", _Dark);
        registry.Record(border, _BorderBrush, "CardStrokeColorDefaultBrush", _Light);

        Assert.HasCount(2, registry.Entries);
        Assert.Contains(new ThemeResourceEntry(border, _Background, "SystemFillColorCriticalBrush", _Dark), registry.Entries);
    }

    [TestMethod]
    public void ClearForgetsEntry()
    {
        var registry = new ThemeResourceRegistry();
        object border = new();
        registry.Record(border, _Background, "CardBackgroundFillColorDefaultBrush", _Light);
        registry.Record(border, _BorderBrush, "CardStrokeColorDefaultBrush", _Light);

        Assert.IsTrue(registry.Forget(border, _Background));
        Assert.IsFalse(registry.Forget(border, _Background), "Forgotten once.");

        ThemeResourceEntry remaining = Assert.ContainsSingle(registry.Entries);
        Assert.AreEqual(_BorderBrush, remaining.Property);
    }

    [TestMethod]
    public void EqualOwnersAreDistinctEntries()
    {
        // Owners compare by reference: two equal objects are two owners.
        var registry = new ThemeResourceRegistry();
        registry.Record("same text", _Background, "Key", _Light);
        registry.Record(new string("same text".AsSpan()), _Background, "Key", _Light);

        Assert.HasCount(2, registry.Entries);
    }

    [TestMethod]
    public void ReleasedOwnersAreForgotten()
    {
        var registry = new ThemeResourceRegistry();
        object released = new();
        object kept = new();
        registry.Record(released, _Background, "Key", _Light);
        registry.Record(released, _BorderBrush, "Key", _Light);
        registry.Record(kept, _Background, "Key", _Light);

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
        registry.Record(pageBorder, _Background, "Key", _Light);
        registry.Record(applicationSetter, "Value:Microsoft.UI.Xaml.Setter, Microsoft.WinUI", "Key", _Light);
        var decided = new List<object>();
        var applied = new List<object>();

        ThemeResourceReapplyResult result = registry.Reapply(
            entry =>
            {
                decided.Add(entry.Owner);
                return _Dark;
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
        Assert.DoesNotContain(entry => entry.Theme != _Dark, registry.Entries);
    }

    [TestMethod]
    public void ReapplySkipsEntriesAlreadyResolvedForTheTheme()
    {
        // Visual Studio sends the same artboard colours again after every new surface: nothing is set again.
        var registry = new ThemeResourceRegistry();
        registry.Record(new object(), _Background, "Key", _Dark);
        int applied = 0;

        ThemeResourceReapplyResult result = registry.Reapply(_ => _Dark, (_, _) => ++applied > 0);

        Assert.AreEqual(new ThemeResourceReapplyResult(1, 0, 0), result);
        Assert.AreEqual(0, applied);
    }

    [TestMethod]
    public void AnEntryThatCouldNotBeAppliedKeepsItsThemeAndIsTriedAgain()
    {
        // The Setter of a style in use is sealed: the value stays the one of the old theme.
        var registry = new ThemeResourceRegistry();
        registry.Record(new object(), "Value:Microsoft.UI.Xaml.Setter, Microsoft.WinUI", "Key", _Light);

        Assert.AreEqual(new ThemeResourceReapplyResult(1, 0, 1), registry.Reapply(_ => _Dark, (_, _) => false));
        Assert.AreEqual(_Light, Assert.ContainsSingle(registry.Entries).Theme);
        Assert.AreEqual(new ThemeResourceReapplyResult(1, 1, 0), registry.Reapply(_ => _Dark, (_, _) => true));
    }

    [TestMethod]
    public void AnOwnerWhoseThemeCannotBeDecidedDoesNotStopThePass()
    {
        // Deciding a theme reads live objects, which can fail for one owner: the entries after it are still evaluated, and
        // the broken one keeps its theme and is tried again. Entries are visited in the order they were recorded.
        var registry = new ThemeResourceRegistry();
        object first = new();
        object broken = new();
        object last = new();
        registry.Record(first, _Background, "Key", _Light);
        registry.Record(broken, _Background, "Key", _Light);
        registry.Record(last, _Background, "Key", _Light);
        var applied = new List<object>();
        var failures = new List<(object Owner, Exception Exception)>();
        var cause = new InvalidOperationException("The owner is gone.");

        ThemeResourceReapplyResult result = registry.Reapply(
            entry => ReferenceEquals(entry.Owner, broken) ? throw cause : _Dark,
            (entry, _) =>
            {
                applied.Add(entry.Owner);
                return true;
            },
            (entry, exception) => failures.Add((entry.Owner, exception))
        );

        Assert.AreEqual(new ThemeResourceReapplyResult(3, 2, 1), result);
        CollectionAssert.AreEquivalent(new[] { first, last }, applied);
        (object Owner, Exception Exception) reported = Assert.ContainsSingle(failures);
        Assert.AreSame(broken, reported.Owner);
        Assert.AreSame(cause, reported.Exception);
        Assert.Contains(new ThemeResourceEntry(broken, _Background, "Key", _Light), registry.Entries);
        Assert.AreEqual(new ThemeResourceReapplyResult(3, 1, 0), registry.Reapply(_ => _Dark, (_, _) => true));
    }

    [TestMethod]
    public void AnApplyThatThrowsDoesNotStopThePassEvenWithoutAFailureReport()
    {
        var registry = new ThemeResourceRegistry();
        object broken = new();
        object last = new();
        registry.Record(broken, _Background, "Key", _Light);
        registry.Record(last, _Background, "Key", _Light);
        var applied = new List<object>();

        ThemeResourceReapplyResult result = registry.Reapply(
            _ => _Dark,
            (entry, _) =>
            {
                if (ReferenceEquals(entry.Owner, broken))
                {
                    throw new InvalidOperationException("The value cannot be applied.");
                }

                applied.Add(entry.Owner);
                return true;
            }
        );

        Assert.AreEqual(new ThemeResourceReapplyResult(2, 1, 1), result);
        CollectionAssert.AreEqual(new[] { last }, applied);
        Assert.Contains(new ThemeResourceEntry(broken, _Background, "Key", _Light), registry.Entries);
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
        var preview = new ThemePreview(_Light, _Light, null, _Light);
        string Decide(ThemeResourceEntry entry) => ThemeResourceThemes.Decide(
            ThemeResourceThemes.Describe(entry.Owner, parents.GetValueOrDefault, FakeScopes.DictionaryTheme, FakeScopes.RequestedTheme),
            preview
        ).Theme;
        var registry = new ThemeResourceRegistry();
        registry.Record(inside, _Background, "Key", _Light);
        registry.Record(beside, _Background, "Key", _Light);
        var applied = new List<object>();

        grid.RequestedTheme = _Dark;
        registry.Reapply(Decide, (entry, _) =>
        {
            applied.Add(entry.Owner);
            return true;
        });

        CollectionAssert.AreEqual(new object[] { inside }, applied);
    }
}
