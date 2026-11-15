// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System.Collections;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.InstanceBuilders.Shared;
using WinUIDesigner.Markup;
using WinUIDesigner.Surface.Common;
using XSurfUwp;

/// <summary>
/// Sends Visual Studio's child actions, serialized and read back with Visual Studio's own serializer, through the
/// translator the surface uses, and checks the collections that result. The collections are plain lists; the replay
/// handles each action the way SurfaceService.Execute does, setters that wait for the end of the batch included.
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
        ]);

        CollectionAssert.AreEqual(new[] { "A", "B", "C" }, Names(panel.Children));
        CollectionAssert.AreEqual(new[] { 1, 3 }, replay.Holes(panel.Children));
        Assert.IsTrue(replay.Tracked.Contains(panel.Children), "The collection becomes part of the document.");
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
        ]);

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
        ]);

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
        ]);

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
        ]);
        CollectionAssert.AreEqual(new[] { "a", "kept", "b" }, Names(panel.Children));
        CollectionAssert.AreEqual(Array.Empty<int>(), replay.Holes(panel.Children));

        // And back: the kept element gets a prefix that does not hold.
        replay.Run(
        [
            new ConnectToLiveTreeAction(_PanelHandle, -1, 0),
            new GetPropertyAction(-1, _Children, -3, 0),
            new RemoveChildAction(-3, 1),
            .. StandIn(-5, 1),
        ]);
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
        ]);

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
        ]);
        replay.Run(
        [
            new ConnectToLiveTreeAction(_PanelHandle, -1, 0),
            new GetPropertyAction(-1, _Children, -3, 0),
            .. StandIn(-5, 1),
            .. Item(-7, "after", 3),
        ]);

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
        ]);

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
        ]);

        CollectionAssert.AreEqual(new[] { "FontSize", "FontStyle", "Margin" }, Names(setters));
        CollectionAssert.AreEqual(new[] { 1 }, replay.Holes(setters));

        // A setter typed between the dropped one and FontStyle.
        replay.Run(
        [
            new ConnectToLiveTreeAction(_PanelHandle, -3, 0),
            .. Item(-5, "Foreground", 2),
        ]);
        CollectionAssert.AreEqual(new[] { "FontSize", "Foreground", "FontStyle", "Margin" }, Names(setters));
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
        ]);

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
        ]);

        CollectionAssert.AreEqual(new[] { "a" }, Names(panel.Children));
        CollectionAssert.AreEqual(new[] { "p" }, Names(plain.Children));
        Assert.IsNull(replay.Get(-5));
        Assert.IsNull(replay.Get(-11));
        Assert.AreEqual(3, replay.Warnings.Count, string.Join(Environment.NewLine, replay.Warnings));
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
        ]);

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
            new CreateInstanceAction(handle, "Item:" + name),
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

    private static string[] Names(IEnumerable<object> items)
    {
        return items.Cast<LiveItem>().Select(item => item.Name).ToArray();
    }

    /// <summary>
    /// A live object created by an action.
    /// </summary>
    private sealed class LiveItem(string name)
    {
        public string Name { get; } = name;

        public override string ToString()
        {
            return Name;
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
    }

    /// <summary>
    /// Gives the translator access to plain lists, as the surface gives it access to WinUI collections.
    /// </summary>
    private sealed class ListAccess : IChildCollectionAccess
    {
        public object GetCollection(object owner)
        {
            return owner is FakePanel panel ? panel.Children : owner;
        }

        public int? GetCount(object collection)
        {
            return (collection as IList)?.Count;
        }

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
    /// Executes the actions the way SurfaceService.Execute does for these kinds.
    /// </summary>
    private sealed class Replay
    {
        private readonly Dictionary<long, object?> _objects = [];

        public Replay()
        {
            Translator = new ChildIndexTranslator(new ListAccess(), Tracked.Add, Warnings.Add);
        }

        public ChildIndexTranslator Translator { get; }

        public Dictionary<long, object> Live { get; } = [];

        public List<object> Tracked { get; } = [];

        public List<string> Warnings { get; } = [];

        public object? Get(long handle)
        {
            return _objects.TryGetValue(handle, out object? value) ? value : null;
        }

        public int[] Holes(object collection)
        {
            return Translator.Maps.TryGet(collection, out ChildIndexMap? map) ? map.Holes.ToArray() : [];
        }

        public void Run(XamlAction[] actions)
        {
            IList<XamlAction> received = XamlActionJsonSerializer.Deserialize(XamlActionJsonSerializer.Serialize(actions));
            Assert.AreEqual(actions.Length, received.Count, "Every action survives Visual Studio's serializer.");

            var deferred = new List<(FakeSetters Collection, int Index, object? Child)>();
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
                    case GetPropertyAction getProperty:
                        _objects[getProperty.PropertyValueHandle] = ((FakePanel)_objects[getProperty.ParentHandle]!).Children;
                        break;
                    case ClearCollectionPropertyAction clear:
                        List<object> cleared = ((FakePanel)_objects[clear.ParentHandle]!).Children;
                        Translator.Forget(cleared);
                        cleared.Clear();
                        break;
                    case AddChildAction addChild:
                        object owner = _objects[addChild.ParentHandle]!;
                        object? child = _objects[addChild.ChildHandle];
                        if (Translator.PlaceChild(owner, addChild.Index, child, collection => deferred.Count(entry => ReferenceEquals(entry.Collection, collection))) is not int liveIndex)
                        {
                            break;
                        }

                        if (owner is FakeSetters setters)
                        {
                            deferred.Add((setters, liveIndex, child));
                        }
                        else
                        {
                            var list = (IList)new ListAccess().GetCollection(owner);
                            list.Insert(liveIndex < 0 || liveIndex > list.Count ? list.Count : liveIndex, child);
                        }

                        break;
                    case RemoveChildAction removeChild:
                        Translator.RemoveChild(_objects[removeChild.ParentHandle]!, removeChild.Index);
                        break;
                    case GetChildAction getChild:
                        object lookupOwner = _objects[getChild.ParentHandle]!;
                        _objects[getChild.ChildHandle] = lookupOwner is DroppedElement ? lookupOwner : Translator.GetChild(lookupOwner, getChild.Index);
                        break;
                    default:
                        Assert.Fail($"The replay does not handle {action.Action}.");
                        break;
                }
            }

            foreach ((FakeSetters collection, int index, object? child) in deferred)
            {
                collection.Insert(index < 0 || index > collection.Count ? collection.Count : index, child!);
            }
        }
    }
}
