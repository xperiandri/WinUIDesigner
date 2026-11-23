// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System.Collections;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.InstanceBuilders.Shared;
using WinUIDesigner.Markup;
using WinUIDesigner.Surface.Common;
using XSurfUwp;

/// <summary>
/// Sends Visual Studio's child actions, serialized and read back with Visual Studio's own serializer, through the
/// translator the surface uses, and checks the collections that result. The collections are plain lists. SurfaceService.Execute
/// needs WinUI and is not compiled here, so the replay is a dispatch of its own; it hands the child actions to the same
/// translator, with the same queue for the setters that wait for the end of the batch, and mirrors nothing else of Execute.
/// </summary>
[TestClass]
public sealed class DroppedChildActionTests
{
    private const string _Children = "Children:Microsoft.UI.Xaml.Controls.Panel";
    private const long _PanelHandle = 100;

    [TestMethod]
    public void ConstructionActionsWithStandInsBuildTheCollectionWithoutThem()
    {
        var replay = new Replay();
        var panel = new FakePanel();
        replay.Live[_PanelHandle] = panel;

        // What Visual Studio's BuildChildren sends for A, a dropped element, B, a dropped element, C.
        replay.Run(
            [
                new ConnectToLiveTreeAction(_PanelHandle, -1, 0),
                new GetPropertyAction(-1, _Children, -3, 0),
                .. Item(-5, "A", 0),
                .. StandIn(-7, 1),
                .. Item(-9, "B", 2),
                .. StandIn(-11, 3),
                .. Item(-13, "C", 4),
            ]
        );

        CollectionAssert.AreEqual(new[] { "A", "B", "C" }, Names(panel.Children));
        CollectionAssert.AreEqual(new[] { 1, 3 }, replay.Holes(panel.Children));
        Assert.Contains(panel.Children, replay.Tracked, "The collection becomes part of the document.");
    }

    [TestMethod]
    public void TheReportedEditLandsAfterTheElementBeforeIt()
    {
        // ConditionalXamlPage.xaml as loaded from prepared XAML: model children 2, 3 and 8 were dropped.
        var replay = new Replay();
        var panel = new FakePanel("heading", "c7", "c99n", "border1", "border2", "newExperience", "button");
        replay.Live[_PanelHandle] = panel;
        replay.Translator.Register(panel, [2, 3, 8]);

        // The two elements typed after "Contract 99 is not present.": a dropped one at 5, a kept one at 6.
        replay.Run(
            [
                new ConnectToLiveTreeAction(_PanelHandle, -1, 0),
                new GetPropertyAction(-1, _Children, -3, 0),
                .. StandIn(-5, 5),
                .. Item(-7, "inserted", 6),
            ]
        );

        CollectionAssert.AreEqual(new[] { "heading", "c7", "c99n", "inserted", "border1", "border2", "newExperience", "button" }, Names(panel.Children));
        CollectionAssert.AreEqual(new[] { 2, 3, 5, 10 }, replay.Holes(panel.Children));
    }

    [TestMethod]
    public void RemovingADroppedElementRemovesNothingAndTheNextIndicesFollow()
    {
        var replay = new Replay();
        var panel = new FakePanel("a", "b", "c");
        replay.Live[_PanelHandle] = panel;
        replay.Translator.Register(panel, [1, 3]);

        // Model: a, dropped, b, dropped, c. The first dropped element is deleted, then b and c are looked up.
        replay.Run(
            [
                new ConnectToLiveTreeAction(_PanelHandle, -1, 0),
                new GetPropertyAction(-1, _Children, -3, 0),
                new RemoveChildAction(-3, 1),
                new GetChildAction(-3, 1, -5),
                new GetChildAction(-3, 2, -7),
                new GetChildAction(-3, 3, -9),
            ]
        );

        CollectionAssert.AreEqual(new[] { "a", "b", "c" }, Names(panel.Children));
        CollectionAssert.AreEqual(new[] { 2 }, replay.Holes(panel.Children));
        Assert.AreEqual("b", (replay.Get(-5) as LiveItem)?.Name);
        Assert.IsInstanceOfType<DroppedElement>(replay.Get(-7), "A lookup at a dropped element gives a stand-in.");
        Assert.AreEqual("c", (replay.Get(-9) as LiveItem)?.Name);
    }

    [TestMethod]
    public void RemovingAnObjectAfterDroppedElementsRemovesThatObject()
    {
        var replay = new Replay();
        var panel = new FakePanel("a", "b", "c");
        replay.Live[_PanelHandle] = panel;
        replay.Translator.Register(panel, [0, 1]);

        replay.Run(
            [
                new ConnectToLiveTreeAction(_PanelHandle, -1, 0),
                new GetPropertyAction(-1, _Children, -3, 0),
                new RemoveChildAction(-3, 3),
            ]
        );

        CollectionAssert.AreEqual(new[] { "a", "c" }, Names(panel.Children));
        CollectionAssert.AreEqual(new[] { 0, 1 }, replay.Holes(panel.Children));
    }

    [TestMethod]
    public void ChangingThePrefixOfAnElementSwapsItWithAStandInInPlace()
    {
        var replay = new Replay();
        var panel = new FakePanel("a", "b");
        replay.Live[_PanelHandle] = panel;
        replay.Translator.Register(panel, [1]);

        // The dropped element gets a prefix that holds: Visual Studio removes it and adds the new node at its index.
        replay.Run(
            [
                new ConnectToLiveTreeAction(_PanelHandle, -1, 0),
                new GetPropertyAction(-1, _Children, -3, 0),
                new RemoveChildAction(-3, 1),
                .. Item(-5, "kept", 1),
            ]
        );
        CollectionAssert.AreEqual(new[] { "a", "kept", "b" }, Names(panel.Children));
        CollectionAssert.AreEqual(Array.Empty<int>(), replay.Holes(panel.Children));

        // And back: the kept element gets a prefix that does not hold.
        replay.Run(
            [
                new ConnectToLiveTreeAction(_PanelHandle, -1, 0),
                new GetPropertyAction(-1, _Children, -3, 0),
                new RemoveChildAction(-3, 1),
                .. StandIn(-5, 1),
            ]
        );
        CollectionAssert.AreEqual(new[] { "a", "b" }, Names(panel.Children));
        CollectionAssert.AreEqual(new[] { 1 }, replay.Holes(panel.Children));
    }

    [TestMethod]
    public void MovingAnElementOverDroppedOnesRemovesAndAddsIt()
    {
        var replay = new Replay();
        var panel = new FakePanel("a", "b", "c");
        var moved = panel.Children[2];
        replay.Live[_PanelHandle] = panel;
        replay.Live[_PanelHandle + 1] = moved;
        replay.Translator.Register(panel, [1, 3]);

        // Model: a, dropped, b, dropped, c. c moves to the front.
        replay.Run(
            [
                new ConnectToLiveTreeAction(_PanelHandle, -1, 0),
                new GetPropertyAction(-1, _Children, -3, 0),
                new RemoveChildAction(-3, 4),
                new ConnectToLiveTreeAction(_PanelHandle + 1, -5, 0),
                new AddChildAction(-3, 0, -5),
            ]
        );

        CollectionAssert.AreEqual(new[] { "c", "a", "b" }, Names(panel.Children));
        CollectionAssert.AreEqual(new[] { 2, 4 }, replay.Holes(panel.Children));
    }

    [TestMethod]
    public void UndoAndRedoOfAnEditNextToDroppedElementsRestoreThePreview()
    {
        var replay = new Replay();
        var panel = new FakePanel("a", "b", "c");
        replay.Live[_PanelHandle] = panel;
        replay.Translator.Register(panel, [1, 3]);

        XamlAction[] insert =
        [
            new ConnectToLiveTreeAction(_PanelHandle, -1, 0),
            new GetPropertyAction(-1, _Children, -3, 0),
            .. Item(-5, "d", 4),
        ];
        XamlAction[] undo =
        [
            new ConnectToLiveTreeAction(_PanelHandle, -1, 0),
            new GetPropertyAction(-1, _Children, -3, 0),
            new RemoveChildAction(-3, 4),
        ];

        replay.Run(insert);
        CollectionAssert.AreEqual(new[] { "a", "b", "d", "c" }, Names(panel.Children));
        replay.Run(undo);
        CollectionAssert.AreEqual(new[] { "a", "b", "c" }, Names(panel.Children));
        replay.Run(insert);
        CollectionAssert.AreEqual(new[] { "a", "b", "d", "c" }, Names(panel.Children));
        CollectionAssert.AreEqual(new[] { 1, 3 }, replay.Holes(panel.Children));
    }

    [TestMethod]
    public void UndoOfTheDeletionOfADroppedElementAddsItsStandInAgain()
    {
        var replay = new Replay();
        var panel = new FakePanel("a", "b");
        replay.Live[_PanelHandle] = panel;
        replay.Translator.Register(panel, [1]);

        replay.Run(
            [
                new ConnectToLiveTreeAction(_PanelHandle, -1, 0),
                new GetPropertyAction(-1, _Children, -3, 0),
                new RemoveChildAction(-3, 1),
            ]
        );
        replay.Run(
            [
                new ConnectToLiveTreeAction(_PanelHandle, -1, 0),
                new GetPropertyAction(-1, _Children, -3, 0),
                .. StandIn(-5, 1),
                .. Item(-7, "after", 3),
            ]
        );

        CollectionAssert.AreEqual(new[] { "a", "b", "after" }, Names(panel.Children));
        CollectionAssert.AreEqual(new[] { 1 }, replay.Holes(panel.Children));
    }

    [TestMethod]
    public void ClearingACollectionForgetsItsDroppedElementsUntilTheyAreAddedAgain()
    {
        var replay = new Replay();
        var panel = new FakePanel("a", "b");
        replay.Live[_PanelHandle] = panel;
        replay.Translator.Register(panel, [0, 2]);

        // Visual Studio rebuilds a collection property: it clears the collection and adds every child again.
        replay.Run(
            [
                new ConnectToLiveTreeAction(_PanelHandle, -1, 0),
                new ClearCollectionPropertyAction(-1, _Children, 0),
                new GetPropertyAction(-1, _Children, -3, 0),
                .. Item(-5, "x", 0),
                .. StandIn(-7, 1),
                .. Item(-9, "y", 2),
            ]
        );

        CollectionAssert.AreEqual(new[] { "x", "y" }, Names(panel.Children));
        CollectionAssert.AreEqual(new[] { 1 }, replay.Holes(panel.Children));
    }

    [TestMethod]
    public void SettersThatWaitForTheEndOfTheBatchCountForLaterIndices()
    {
        // WinUI only accepts a complete setter, so the surface adds setters when the batch ends. Their positions are
        // decided when the action arrives, with the setters still waiting counted in.
        var replay = new Replay();
        var setters = new FakeSetters();
        replay.Live[_PanelHandle] = setters;

        replay.Run(
            [
                new ConnectToLiveTreeAction(_PanelHandle, -3, 0),
                .. Item(-5, "FontSize", 0),
                .. StandIn(-7, 1),
                .. Item(-9, "FontStyle", 2),
                .. Item(-11, "Margin", 3),
            ]
        );

        CollectionAssert.AreEqual(new[] { "FontSize", "FontStyle", "Margin" }, Names(setters));
        CollectionAssert.AreEqual(new[] { 1 }, replay.Holes(setters));

        // A setter typed between the dropped one and FontStyle.
        replay.Run(
            [
                new ConnectToLiveTreeAction(_PanelHandle, -3, 0),
                .. Item(-5, "Foreground", 2),
            ]
        );
        CollectionAssert.AreEqual(new[] { "FontSize", "Foreground", "FontStyle", "Margin" }, Names(setters));
    }

    [TestMethod]
    public void ARemovalSeesTheSettersThatWaitForTheEndOfTheBatch()
    {
        // Visual Studio adds X at 1 and then removes the setter at 3, which is C once X is in. A removal that ran on
        // the collection without X would point past its end, and X would end up one place off.
        var replay = new Replay();
        var setters = new FakeSetters("A", "B", "C");
        replay.Live[_PanelHandle] = setters;

        replay.Run(
            [
                new ConnectToLiveTreeAction(_PanelHandle, -3, 0),
                .. Item(-5, "X", 1),
                new RemoveChildAction(-3, 3),
            ]
        );

        CollectionAssert.AreEqual(new[] { "A", "X", "B" }, Names(setters));
        Assert.IsEmpty(replay.Warnings);
    }

    [TestMethod]
    public void ALookupAndARemovalCountTheWaitingSettersAmongHoles()
    {
        // Model: A, a dropped setter, B. X is added at the end of the model and waits; it is then looked up and removed.
        var replay = new Replay();
        var setters = new FakeSetters("A", "B");
        replay.Live[_PanelHandle] = setters;
        replay.Translator.Register(setters, [1]);

        replay.Run(
            [
                new ConnectToLiveTreeAction(_PanelHandle, -3, 0),
                .. Item(-5, "X", 3),
                new GetChildAction(-3, 3, -7),
                new RemoveChildAction(-3, 3),
            ]
        );

        Assert.AreEqual("X", (replay.Get(-7) as LiveItem)?.Name, "The lookup finds the setter that was waiting.");
        CollectionAssert.AreEqual(new[] { "A", "B" }, Names(setters));
        CollectionAssert.AreEqual(new[] { 1 }, replay.Holes(setters));
        Assert.IsEmpty(replay.Warnings);
    }

    [TestMethod]
    public void ActionsBeneathAStandInChangeNothingAndEndAtIt()
    {
        var replay = new Replay();
        var panel = new FakePanel("a");
        replay.Live[_PanelHandle] = panel;

        replay.Run(
            [
                new ConnectToLiveTreeAction(_PanelHandle, -1, 0),
                new GetPropertyAction(-1, _Children, -3, 0),
                .. StandIn(-5, 1),
                new GetChildAction(-3, 1, -7),
                new GetPropertyAction(-7, _Children, -9, 0),
                new GetChildAction(-9, 0, -11),
                new CreateInstanceAction(-13, "Item:late"),
                new AddChildAction(-9, 0, -13),
                new RemoveChildAction(-9, 0),
            ]
        );

        CollectionAssert.AreEqual(new[] { "a" }, Names(panel.Children));
        CollectionAssert.AreEqual(new[] { 1 }, replay.Holes(panel.Children));
        Assert.IsInstanceOfType<DroppedElement>(replay.Get(-7), "A lookup at a dropped element gives a stand-in.");
        Assert.AreSame(replay.Get(-7), replay.Get(-9), "A property of a stand-in is the stand-in.");
        Assert.AreSame(replay.Get(-7), replay.Get(-11), "A child of a stand-in is the stand-in.");
        Assert.IsEmpty(replay.Warnings);
    }

    [TestMethod]
    public void AnEditInsideADroppedMergedDictionaryEndsAtTheStandIn()
    {
        // Page.Resources: a dropped dictionary with a brush, then a kept one with a brush under the same key. Visual
        // Studio reaches the brush through the dictionary, and the dictionary is the stand-in of the dropped one.
        var replay = new Replay();
        var merged = new FakePanel();
        var kept = new FakeDictionary();
        var keptBrush = new LiveItem("keptBrush");
        kept.Entries["Accent"] = keptBrush;
        merged.Children.Add(kept);
        replay.Live[_PanelHandle] = merged;
        replay.Live[_PanelHandle + 1] = "Accent";
        replay.Translator.Register(merged, [0]);

        // The Color of the brush in the dropped dictionary changes.
        replay.Run(
            [
                new ConnectToLiveTreeAction(_PanelHandle, -1, 0),
                new GetPropertyAction(-1, _Children, -3, 0),
                new GetChildAction(-3, 0, -5),
                new ConnectToLiveTreeAction(_PanelHandle + 1, -7, 0),
                new GetDictionaryEntryAction(-5, -7, -9),
                new CreateInstanceAction(-11, "Item:red"),
                new SetPropertyAction(-9, "Color", -11, 0),
            ]
        );

        Assert.IsInstanceOfType<DroppedElement>(replay.Get(-5), "The dropped dictionary is a stand-in.");
        Assert.IsInstanceOfType<DroppedElement>(replay.Get(-9), "An entry of a stand-in is the stand-in again.");
        Assert.IsEmpty(keptBrush.Properties, "The brush of the kept dictionary is not the one that was edited.");
    }

    [TestMethod]
    public void ChangesOfTheEntriesOfADroppedMergedDictionaryChangeNothing()
    {
        var replay = new Replay();
        var merged = new FakePanel();
        var kept = new FakeDictionary();
        var keptBrush = new LiveItem("keptBrush");
        kept.Entries["Accent"] = keptBrush;
        merged.Children.Add(kept);
        replay.Live[_PanelHandle] = merged;
        replay.Live[_PanelHandle + 1] = "Accent";
        replay.Translator.Register(merged, [0]);

        // Renaming the key of the entry removes it and adds it again; replacing the brush updates the value. The value
        // handle -99 has no object: nothing may resolve the value of an entry of a stand-in.
        replay.Run(
            [
                new ConnectToLiveTreeAction(_PanelHandle, -1, 0),
                new GetPropertyAction(-1, _Children, -3, 0),
                new GetChildAction(-3, 0, -5),
                new ConnectToLiveTreeAction(_PanelHandle + 1, -7, 0),
                new RemoveDictionaryEntryAction(-5, -7),
                new AddDictionaryEntryAction(-5, -7, -99),
                new UpdateDictionaryValueAction(-5, -7, -99),
            ]
        );

        Assert.HasCount(1, kept.Entries);
        Assert.AreSame(keptBrush, kept.Entries["Accent"]);
    }

    [TestMethod]
    public void DictionaryActionsOnALiveDictionaryAreApplied()
    {
        var replay = new Replay();
        var dictionary = new FakeDictionary();
        var brush = new LiveItem("brush");
        dictionary.Entries["Accent"] = brush;
        replay.Live[_PanelHandle] = dictionary;
        replay.Live[_PanelHandle + 1] = "Accent";
        replay.Live[_PanelHandle + 2] = "Other";

        replay.Run(
            [
                new ConnectToLiveTreeAction(_PanelHandle, -1, 0),
                new ConnectToLiveTreeAction(_PanelHandle + 1, -3, 0),
                new ConnectToLiveTreeAction(_PanelHandle + 2, -5, 0),
                new GetDictionaryEntryAction(-1, -3, -7),
                new CreateInstanceAction(-9, "Item:red"),
                new SetPropertyAction(-7, "Color", -9, 0),
                new AddDictionaryEntryAction(-1, -5, -9),
                new NullReferenceAction(-11),
                new UpdateDictionaryValueAction(-1, -3, -11),
            ]
        );

        Assert.AreSame(brush, replay.Get(-7), "A lookup in a live dictionary finds the entry.");
        Assert.AreEqual("red", (brush.Properties["Color"] as LiveItem)?.Name);
        Assert.AreEqual("red", (dictionary.Entries["Other"] as LiveItem)?.Name, "An added entry is in the dictionary.");
        Assert.IsNull(dictionary.Entries["Accent"], "A replaced value can be null.");

        replay.Run(
            [
                new ConnectToLiveTreeAction(_PanelHandle, -1, 0),
                new ConnectToLiveTreeAction(_PanelHandle + 2, -3, 0),
                new RemoveDictionaryEntryAction(-1, -3),
            ]
        );

        Assert.IsFalse(dictionary.Entries.ContainsKey("Other"), "A removed entry is gone.");
    }

    [TestMethod]
    public void AnEntryThatStandsInForADroppedElementIsNotAddedToALiveDictionary()
    {
        var replay = new Replay();
        var dictionary = new FakeDictionary();
        var brush = new LiveItem("brush");
        dictionary.Entries["Accent"] = brush;
        replay.Live[_PanelHandle] = dictionary;
        replay.Live[_PanelHandle + 1] = "Accent";
        replay.Live[_PanelHandle + 2] = "Other";

        replay.Run(
            [
                new ConnectToLiveTreeAction(_PanelHandle, -1, 0),
                new ConnectToLiveTreeAction(_PanelHandle + 1, -3, 0),
                new ConnectToLiveTreeAction(_PanelHandle + 2, -5, 0),
                new CreateInstanceAction(-7, DroppedElementStandIn.TypeName),
                new AddDictionaryEntryAction(-1, -5, -7),
                new UpdateDictionaryValueAction(-1, -3, -7),
            ]
        );

        Assert.HasCount(1, dictionary.Entries);
        Assert.AreSame(brush, dictionary.Entries["Accent"]);
    }

    [TestMethod]
    public void ACollectionWithoutDroppedElementsKeepsTheIndicesAsTheyAre()
    {
        var replay = new Replay();
        var panel = new FakePanel("a", "b", "c");
        replay.Live[_PanelHandle] = panel;

        replay.Run(
            [
                new ConnectToLiveTreeAction(_PanelHandle, -1, 0),
                new GetPropertyAction(-1, _Children, -3, 0),
                .. Item(-5, "x", 1),
                new RemoveChildAction(-3, 3),
                new GetChildAction(-3, 0, -7),
            ]
        );

        CollectionAssert.AreEqual(new[] { "a", "x", "b" }, Names(panel.Children));
        Assert.AreEqual("a", (replay.Get(-7) as LiveItem)?.Name);
        Assert.AreEqual(0, replay.Translator.Maps.Count, "No collection needed a map.");
    }

    [TestMethod]
    public void IndicesThatPointNowhereChangeNothingAndAreReported()
    {
        var replay = new Replay();
        var panel = new FakePanel("a");
        var plain = new FakePanel("p");
        replay.Live[_PanelHandle] = panel;
        replay.Live[_PanelHandle + 1] = plain;
        replay.Translator.Register(panel, [1]);

        replay.Run(
            [
                new ConnectToLiveTreeAction(_PanelHandle, -1, 0),
                new GetPropertyAction(-1, _Children, -3, 0),
                new RemoveChildAction(-3, 7),
                new RemoveChildAction(-3, -1),
                new GetChildAction(-3, 7, -5),
                new ConnectToLiveTreeAction(_PanelHandle + 1, -7, 0),
                new GetPropertyAction(-7, _Children, -9, 0),
                new RemoveChildAction(-9, 4),
                new GetChildAction(-9, 4, -11),
            ]
        );

        CollectionAssert.AreEqual(new[] { "a" }, Names(panel.Children));
        CollectionAssert.AreEqual(new[] { "p" }, Names(plain.Children));
        Assert.IsNull(replay.Get(-5));
        Assert.IsNull(replay.Get(-11));
        Assert.HasCount(3, replay.Warnings, string.Join(Environment.NewLine, replay.Warnings));
        Assert.IsTrue(replay.Warnings.All(message => message.Contains("points past its end", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void AnInsertionPastTheEndAppendsAfterTheDroppedElements()
    {
        var replay = new Replay();
        var panel = new FakePanel("a");
        replay.Live[_PanelHandle] = panel;
        replay.Translator.Register(panel, [1]);

        replay.Run(
            [
                new ConnectToLiveTreeAction(_PanelHandle, -1, 0),
                new GetPropertyAction(-1, _Children, -3, 0),
                .. Item(-5, "last", 42),
                .. Item(-7, "first", 0),
            ]
        );

        CollectionAssert.AreEqual(new[] { "first", "a", "last" }, Names(panel.Children));
        CollectionAssert.AreEqual(new[] { 2 }, replay.Holes(panel.Children));
    }

    [TestMethod]
    public void ReleasingTheObjectsOfADocumentForgetsTheirMaps()
    {
        var replay = new Replay();
        var panel = new FakePanel("a");
        var other = new FakePanel("b");
        replay.Translator.Register(panel, [0]);
        replay.Translator.Register(other, [1]);

        replay.Translator.Release([panel.Children]);

        Assert.IsFalse(replay.Translator.Maps.TryGet(panel.Children, out _));
        Assert.IsTrue(replay.Translator.Maps.TryGet(other.Children, out _));
        replay.Translator.Clear();
        Assert.AreEqual(0, replay.Translator.Maps.Count);
    }

    private static XamlAction[] Item(long handle, string name, int index)
    {
        return
        [
            new CreateInstanceAction(handle, $"Item:{name}"),
            new AddChildAction(-3, index, handle),
        ];
    }

    private static XamlAction[] StandIn(long handle, int index)
    {
        return
        [
            new CreateInstanceAction(handle, DroppedElementStandIn.TypeName),
            new AddChildAction(-3, index, handle),
        ];
    }

    private static string[] Names(IEnumerable<object> items) => items.Cast<LiveItem>().Select(item => item.Name).ToArray();

    /// <summary>
    /// A live object created by an action.
    /// </summary>
    private sealed class LiveItem(string name)
    {
        public string Name { get; } = name;

        public Dictionary<string, object?> Properties { get; } = [];

        public override string ToString() => Name;
    }

    /// <summary>
    /// A live dictionary, such as a resource dictionary of a page.
    /// </summary>
    private sealed class FakeDictionary
    {
        public Dictionary<object, object?> Entries { get; } = [];
    }

    /// <summary>
    /// Gives the dictionary actions access to <see cref="FakeDictionary"/> objects. Like the runtime utilities of the
    /// surface, it answers a lookup in anything else with null and fails every change of it.
    /// </summary>
    private sealed class DictionaryAccess : IDictionaryEntryAccess
    {
        public object? GetEntry(object dictionary, object? key)
        {
            return dictionary is FakeDictionary live ? live.Entries.GetValueOrDefault(key!) : null;
        }

        public void AddEntry(object dictionary, object? key, object? value) => Require(dictionary).Entries.Add(key!, value);

        public void ReplaceEntry(object dictionary, object? key, object? value) => Require(dictionary).Entries[key!] = value;

        public void RemoveEntry(object dictionary, object? key) => Require(dictionary).Entries.Remove(key!);

        private static FakeDictionary Require(object dictionary)
        {
            return dictionary as FakeDictionary
                ?? throw new InvalidOperationException($"'{dictionary.GetType().FullName}' is not a supported dictionary.");
        }
    }

    /// <summary>
    /// An object that holds its children, as a panel does.
    /// </summary>
    private sealed class FakePanel
    {
        public FakePanel(params string[] children)
        {
            Children.AddRange(children.Select(name => (object)new LiveItem(name)));
        }

        public List<object> Children { get; } = [];
    }

    /// <summary>
    /// A collection whose children are added when the batch ends, as the setters of a style are.
    /// </summary>
    private sealed class FakeSetters : List<object>
    {
        public FakeSetters(params string[] names)
        {
            AddRange(names.Select(name => (object)new LiveItem(name)));
        }
    }

    /// <summary>
    /// A setter that waits for the end of its batch, at the live index its action was given.
    /// </summary>
    private readonly record struct PendingSetter(FakeSetters Collection, int Index, object? Child);

    /// <summary>
    /// Gives the translator access to plain lists, as the surface gives it access to WinUI collections.
    /// </summary>
    private sealed class ListAccess : IChildCollectionAccess
    {
        public object GetCollection(object owner) => owner is FakePanel panel ? panel.Children : owner;

        public int? GetCount(object collection) => (collection as IList)?.Count;

        public bool RemoveAt(object owner, int liveIndex)
        {
            var list = (IList)GetCollection(owner);
            if (liveIndex < 0 || liveIndex >= list.Count)
            {
                return false;
            }

            list.RemoveAt(liveIndex);
            return true;
        }

        public object? GetAt(object owner, int liveIndex)
        {
            var list = (IList)GetCollection(owner);
            return liveIndex >= 0 && liveIndex < list.Count ? list[liveIndex] : null;
        }
    }

    /// <summary>
    /// Executes the child actions of a batch through the translator and the queue of waiting setters that
    /// SurfaceService.Execute uses. The other kinds of action are mirrored only as far as the sequences need them.
    /// </summary>
    private sealed class Replay
    {
        private readonly Dictionary<long, object?> _objects = [];
        private readonly DictionaryEntryActions _dictionaries = new(new DictionaryAccess());

        public Replay()
        {
            Translator = new ChildIndexTranslator(new ListAccess(), Tracked.Add, Warnings.Add);
        }

        public ChildIndexTranslator Translator { get; }

        public Dictionary<long, object> Live { get; } = [];

        public List<object> Tracked { get; } = [];

        public List<string> Warnings { get; } = [];

        public object? Get(long handle) => _objects.TryGetValue(handle, out object? value) ? value : null;

        public int[] Holes(object collection) => Translator.Maps.TryGet(collection, out ChildIndexMap? map) ? map.Holes.ToArray() : [];

        public void Run(XamlAction[] actions)
        {
            IList<XamlAction> received = XamlActionJsonSerializer.Deserialize(XamlActionJsonSerializer.Serialize(actions));
            Assert.HasCount(actions.Length, received, "Every action survives Visual Studio's serializer.");

            var pending = new DeferredInsertionQueue<PendingSetter>(entry => entry.Collection);
            foreach (XamlAction action in received)
            {
                switch (action)
                {
                    case ConnectToLiveTreeAction connect:
                        _objects[connect.ProxyHandle] = Live[connect.LiveHandle];
                        break;
                    case CreateInstanceAction create when DroppedElementStandIn.IsTypeName(create.TypeName):
                        _objects[create.LiveHandle] = new DroppedElement();
                        break;
                    case CreateInstanceAction create:
                        _objects[create.LiveHandle] = new LiveItem(create.TypeName["Item:".Length..]);
                        break;
                    case NullReferenceAction nullReference:
                        _objects[nullReference.LiveHandle] = null;
                        break;
                    case GetPropertyAction getProperty:
                        // The stand-in ends the path; this guard is a copy of the one in Execute.
                        object valueOwner = _objects[getProperty.ParentHandle]!;
                        _objects[getProperty.PropertyValueHandle] = valueOwner is DroppedElement ? valueOwner : ((FakePanel)valueOwner).Children;
                        break;
                    case ClearCollectionPropertyAction clear:
                        List<object> cleared = ((FakePanel)_objects[clear.ParentHandle]!).Children;
                        Translator.Forget(cleared);
                        cleared.Clear();
                        break;
                    case AddChildAction addChild:
                        object owner = _objects[addChild.ParentHandle]!;
                        object? child = owner is DroppedElement ? null : _objects[addChild.ChildHandle];
                        if (Translator.PlaceChild(owner, addChild.Index, child, pending.CountFor) is not int liveIndex)
                        {
                            break;
                        }

                        if (owner is FakeSetters setters)
                        {
                            pending.Add(new PendingSetter(setters, liveIndex, child));
                        }
                        else
                        {
                            Insert((IList)new ListAccess().GetCollection(owner), liveIndex, child);
                        }

                        break;
                    case RemoveChildAction removeChild:
                        Translator.RemoveChild(_objects[removeChild.ParentHandle]!, removeChild.Index, collection => InsertPending(pending, collection));
                        break;
                    case GetChildAction getChild:
                        _objects[getChild.ChildHandle] = Translator.GetChild(_objects[getChild.ParentHandle]!, getChild.Index, collection => InsertPending(pending, collection));
                        break;
                    case GetDictionaryEntryAction getEntry:
                        _objects[getEntry.ValueHandle] = _dictionaries.GetEntry(_objects[getEntry.DictionaryHandle]!, _objects[getEntry.KeyHandle]);
                        break;
                    case AddDictionaryEntryAction addEntry:
                        _dictionaries.AddEntry(_objects[addEntry.DictionaryHandle]!, _objects[addEntry.KeyHandle], () => _objects[addEntry.ValueHandle]);
                        break;
                    case UpdateDictionaryValueAction updateValue:
                        _dictionaries.ReplaceEntry(_objects[updateValue.DictionaryHandle]!, _objects[updateValue.KeyHandle], () => _objects[updateValue.ValueHandle]);
                        break;
                    case RemoveDictionaryEntryAction removeEntry:
                        _dictionaries.RemoveEntry(_objects[removeEntry.DictionaryHandle]!, _objects[removeEntry.KeyHandle]);
                        break;
                    case SetPropertyAction setProperty:
                        // Like Execute: a handle without an object fails the batch, a stand-in takes no property. These are copies.
                        object propertyOwner = _objects[setProperty.ParentHandle] ?? throw new KeyNotFoundException($"No live object is registered for handle {setProperty.ParentHandle}.");
                        if (propertyOwner is not DroppedElement)
                        {
                            ((LiveItem)propertyOwner).Properties[setProperty.FullPropertyName] = _objects[setProperty.PropertyValueHandle];
                        }

                        break;
                    default:
                        Assert.Fail($"The replay does not handle {action.Action}.");
                        break;
                }
            }

            foreach (PendingSetter setter in pending.TakeAll())
            {
                Insert(setter.Collection, setter.Index, setter.Child);
            }
        }

        private static void InsertPending(DeferredInsertionQueue<PendingSetter> pending, object collection)
        {
            pending.RunAfterPendingInsertions(collection, setter => Insert(setter.Collection, setter.Index, setter.Child), static () => { });
        }

        private static void Insert(IList list, int index, object? child)
        {
            list.Insert(index < 0 || index > list.Count ? list.Count : index, child);
        }
    }
}
