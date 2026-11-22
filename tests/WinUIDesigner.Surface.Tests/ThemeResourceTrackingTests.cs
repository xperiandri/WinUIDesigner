// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using Microsoft.VisualStudio.DesignTools.RuntimeHost.InstanceBuilders.Shared;
using WinUIDesigner.Surface.Common;

/// <summary>
/// Which properties the registry remembers as an action is executed, and when an action moves the theme of the references
/// below its owner. The surface follows every action it executes through the function that these tests run.
/// </summary>
[TestClass]
public sealed class ThemeResourceTrackingTests
{
    private const long _OwnerHandle = 7;
    private const string _Background = "Background:Microsoft.UI.Xaml.Controls.Border, Microsoft.WinUI";
    private const string _RequestedTheme = "RequestedTheme:Microsoft.UI.Xaml.FrameworkElement, Microsoft.WinUI";

    [TestMethod]
    public void APropertySetFromAThemeReferenceIsRemembered()
    {
        var registry = new ThemeResourceRegistry();
        object border = new();

        bool movesTheme = Track(registry, Set(_Background), new ThemeResourceUse(border, "Key", "Dark"), border);

        Assert.IsFalse(movesTheme);
        Assert.AreEqual(new ThemeResourceEntry(border, _Background, "Key", "Dark"), Assert.ContainsSingle(registry.Entries));
    }

    [TestMethod]
    public void AnotherValueForThePropertyForgetsTheEntry()
    {
        // The reference was replaced by a value that is not a theme reference: it no longer decides the property.
        var registry = new ThemeResourceRegistry();
        object border = new();
        registry.Record(border, _Background, "Key", "Dark");

        Track(registry, Set(_Background), evaluated: null, border);

        Assert.IsEmpty(registry.Entries);
    }

    [TestMethod]
    public void ClearingThePropertyForgetsTheEntry()
    {
        // Only a set remembers a reference, even when one was evaluated at the owner.
        var registry = new ThemeResourceRegistry();
        object border = new();
        registry.Record(border, _Background, "Key", "Dark");

        Track(registry, Clear(_Background), new ThemeResourceUse(border, "Key", "Dark"), border);

        Assert.IsEmpty(registry.Entries);
    }

    [TestMethod]
    public void AReferenceEvaluatedAtAnotherOwnerIsNotRecordedForThisOne()
    {
        // What the action set is not what the evaluation was for: the old entry is forgotten and nothing is remembered.
        var registry = new ThemeResourceRegistry();
        object border = new();
        registry.Record(border, _Background, "Key", "Dark");

        Track(registry, Set(_Background), new ThemeResourceUse(new object(), "OtherKey", "Dark"), border);

        Assert.IsEmpty(registry.Entries);
    }

    [TestMethod]
    public void AnOwnerOutsideEveryDocumentIsNotRemembered()
    {
        // Entries are forgotten when their document is released: an owner that belongs to none would never be.
        var registry = new ThemeResourceRegistry();
        object border = new();

        Track(registry, Set(_Background), new ThemeResourceUse(border, "Key", "Dark"), border, document: 0);

        Assert.IsEmpty(registry.Entries);
    }

    [TestMethod]
    public void OtherActionsLeaveTheRegistryAlone()
    {
        var registry = new ThemeResourceRegistry();
        object border = new();
        registry.Record(border, _Background, "Key", "Dark");
        XamlAction[] others =
        [
            new AddChildAction(_OwnerHandle, 0, 8),
            new CreateInstanceAction(_OwnerHandle, "Microsoft.UI.Xaml.Controls.Border, Microsoft.WinUI", 0, 0),
            new SetSurfaceContentAction(1, _OwnerHandle),
        ];

        foreach (XamlAction action in others)
        {
            Assert.IsFalse(Track(registry, action, new ThemeResourceUse(border, "Key", "Dark"), border), action.GetType().Name);
        }

        Assert.AreEqual(new ThemeResourceEntry(border, _Background, "Key", "Dark"), Assert.ContainsSingle(registry.Entries));
    }

    [TestMethod]
    public void AnOwnerThatNoObjectStandsForLeavesTheRegistryAlone()
    {
        var registry = new ThemeResourceRegistry();
        object border = new();
        registry.Record(border, _Background, "Key", "Dark");

        bool movesTheme = Track(registry, Set(_RequestedTheme), new ThemeResourceUse(border, "Key", "Dark"), owner: null);

        Assert.IsFalse(movesTheme, "There is no owner below which a reference could move.");
        Assert.HasCount(1, registry.Entries);
    }

    [TestMethod]
    public void ARequestedThemeSetOrClearedMovesTheThemeOfTheReferencesBelow()
    {
        var registry = new ThemeResourceRegistry();
        object grid = new();

        Assert.IsTrue(Track(registry, Set(_RequestedTheme), evaluated: null, grid));
        Assert.IsTrue(Track(registry, Clear(_RequestedTheme), evaluated: null, grid));
        Assert.IsFalse(Track(registry, Set(_Background), evaluated: null, grid));
        Assert.IsFalse(Track(registry, Clear(_Background), evaluated: null, grid));
    }

    private static SetPropertyAction Set(string property) => new(_OwnerHandle, property, -41, 0);

    private static ClearPropertyAction Clear(string property) => new(_OwnerHandle, property, 0);

    private static bool Track(ThemeResourceRegistry registry, XamlAction action, ThemeResourceUse? evaluated, object? owner, int document = 1)
    {
        // The owner of every action here is the handle _OwnerHandle.
        return ThemeResourceTracking.Track(registry, action, evaluated, handle => handle == _OwnerHandle ? owner : null, _ => document);
    }
}
