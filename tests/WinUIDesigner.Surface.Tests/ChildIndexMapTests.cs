// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using WinUIDesigner.Surface.Common;

/// <summary>
/// Pins how the child indices of Visual Studio's document model translate into indices of a live collection that
/// lacks the elements a namespace policy dropped. A hole is the model position of such an element.
/// </summary>
[TestClass]
public sealed class ChildIndexMapTests
{
    [TestMethod]
    public void WithoutHolesEveryIndexIsTheLiveIndex()
    {
        var map = new ChildIndexMap();

        Assert.AreEqual(ChildSlot.Live(0), map.Find(0, liveCount: 3));
        Assert.AreEqual(ChildSlot.Live(2), map.Find(2, liveCount: 3));
        Assert.AreEqual(2, map.InsertItem(2, liveCount: 3));
        Assert.AreEqual(ChildSlot.Live(1), map.RemoveAt(1, liveCount: 4));
        Assert.IsEmpty(map.Holes);
    }

    [TestMethod]
    [DataRow(3, 3)]
    [DataRow(4, 3)]
    [DataRow(int.MaxValue, 3)]
    [DataRow(-1, 3)]
    public void InsertionPastTheEndOrNegativeAppends(int modelIndex, int expectedLiveIndex)
    {
        var map = new ChildIndexMap();

        Assert.AreEqual(expectedLiveIndex, map.InsertItem(modelIndex, liveCount: 3));
    }

    [TestMethod]
    [DataRow(0, 0)]
    [DataRow(1, 1)]
    [DataRow(2, 2)]
    [DataRow(3, 2)]
    [DataRow(4, 2)]
    [DataRow(5, 3)]
    [DataRow(6, 4)]
    public void InsertionTranslatesPastTheHolesBeforeIt(int modelIndex, int expectedLiveIndex)
    {
        // Model: L0 L1 H H L2 L3 (holes at 2 and 3), live: L0 L1 L2 L3.
        var map = new ChildIndexMap([2, 3]);

        Assert.AreEqual(expectedLiveIndex, map.InsertItem(modelIndex, liveCount: 4));
    }

    [TestMethod]
    public void InsertionAtAHoleGoesBeforeItAndMovesIt()
    {
        var map = new ChildIndexMap([2]);

        Assert.AreEqual(2, map.InsertItem(2, liveCount: 3));

        CollectionAssert.AreEqual(new[] { 3 }, Holes(map));
    }

    [TestMethod]
    public void InsertionMovesOnlyTheHolesAtAndAfterIt()
    {
        var map = new ChildIndexMap([1, 4, 6]);

        map.InsertItem(4, liveCount: 4);

        CollectionAssert.AreEqual(new[] { 1, 5, 7 }, Holes(map));
    }

    [TestMethod]
    [DataRow(0, new[] { 0, 2, 4 })]
    [DataRow(1, new[] { 1, 2, 4 })]
    [DataRow(2, new[] { 1, 2, 4 })]
    [DataRow(3, new[] { 1, 3, 4 })]
    [DataRow(5, new[] { 1, 3, 5 })]
    public void HoleInsertionMovesTheHolesAtAndAfterIt(int modelIndex, int[] expectedHoles)
    {
        // Model: L H L H L (holes at 1 and 3), five entries.
        var map = new ChildIndexMap([1, 3]);

        map.InsertHole(modelIndex, liveCount: 3);

        CollectionAssert.AreEqual(expectedHoles, Holes(map));
    }

    [TestMethod]
    [DataRow(9)]
    [DataRow(-1)]
    public void HoleInsertionPastTheEndOrNegativeAppends(int modelIndex)
    {
        var map = new ChildIndexMap([1]);

        Assert.AreEqual(4, map.InsertHole(modelIndex, liveCount: 3));

        CollectionAssert.AreEqual(new[] { 1, 4 }, Holes(map));
    }

    [TestMethod]
    public void RemovingAHoleLeavesTheLiveCollectionAndMovesTheLaterHoles()
    {
        var map = new ChildIndexMap([1, 3, 5]);

        Assert.AreEqual(ChildSlot.Hole, map.RemoveAt(3, liveCount: 4));

        CollectionAssert.AreEqual(new[] { 1, 4 }, Holes(map));
    }

    [TestMethod]
    [DataRow(0, 0, new[] { 0, 2 })]
    [DataRow(2, 1, new[] { 1, 2 })]
    [DataRow(4, 2, new[] { 1, 3 })]
    public void RemovingAnObjectTranslatesItsIndexAndMovesTheLaterHoles(int modelIndex, int expectedLiveIndex, int[] expectedHoles)
    {
        // Model: L0 H L1 H L2 (holes at 1 and 3).
        var map = new ChildIndexMap([1, 3]);

        Assert.AreEqual(ChildSlot.Live(expectedLiveIndex), map.RemoveAt(modelIndex, liveCount: 3));

        CollectionAssert.AreEqual(expectedHoles, Holes(map));
    }

    [TestMethod]
    [DataRow(5)]
    [DataRow(6)]
    [DataRow(-1)]
    public void RemovingOutsideTheModelChangesNothing(int modelIndex)
    {
        var map = new ChildIndexMap([1, 3]);

        Assert.AreEqual(ChildSlot.Outside, map.RemoveAt(modelIndex, liveCount: 3));

        CollectionAssert.AreEqual(new[] { 1, 3 }, Holes(map));
    }

    [TestMethod]
    public void LookupTellsHolesFromObjectsWithoutChangingAnything()
    {
        // Model: L0 H L1 H L2.
        var map = new ChildIndexMap([1, 3]);

        Assert.AreEqual(ChildSlot.Live(0), map.Find(0, liveCount: 3));
        Assert.AreEqual(ChildSlot.Hole, map.Find(1, liveCount: 3));
        Assert.AreEqual(ChildSlot.Live(1), map.Find(2, liveCount: 3));
        Assert.AreEqual(ChildSlot.Hole, map.Find(3, liveCount: 3));
        Assert.AreEqual(ChildSlot.Live(2), map.Find(4, liveCount: 3));
        Assert.AreEqual(ChildSlot.Outside, map.Find(5, liveCount: 3));
        Assert.AreEqual(ChildSlot.Outside, map.Find(-1, liveCount: 3));
        CollectionAssert.AreEqual(new[] { 1, 3 }, Holes(map));
    }

    [TestMethod]
    public void ConstructionSortsTheHolesAndIgnoresDuplicatesAndNegativePositions()
    {
        var map = new ChildIndexMap([8, 2, -4, 3, 2]);

        CollectionAssert.AreEqual(new[] { 2, 3, 8 }, Holes(map));
    }

    [TestMethod]
    public void HolesPastTheEndOfACollectionThatShrankMoveToItsEnd()
    {
        // The collection lost objects without Visual Studio's knowledge: the holes cannot lie past its end.
        var map = new ChildIndexMap([2, 9]);

        Assert.AreEqual(ChildSlot.Live(1), map.Find(1, liveCount: 2));
        Assert.AreEqual(ChildSlot.Hole, map.Find(2, liveCount: 2));
        Assert.AreEqual(ChildSlot.Hole, map.Find(3, liveCount: 2));
        Assert.AreEqual(ChildSlot.Outside, map.Find(4, liveCount: 2));
        CollectionAssert.AreEqual(new[] { 2, 3 }, Holes(map));
    }

    [TestMethod]
    public void AnObjectNeverTranslatesPastTheEndOfTheLiveCollection()
    {
        var map = new ChildIndexMap([0, 1, 2]);

        // Every model position is a hole or an object; an object is always within the live collection.
        for (int modelIndex = 0; modelIndex < 5; modelIndex++)
        {
            ChildSlot slot = map.Find(modelIndex, liveCount: 2);
            Assert.IsTrue(slot.Kind != ChildSlotKind.Live || (slot.LiveIndex >= 0 && slot.LiveIndex < 2), $"Index {modelIndex} gave {slot}.");
        }
    }

    [TestMethod]
    public void TheReportedEditLandsAfterTheElementBeforeIt()
    {
        // The StackPanel of ConditionalXamlPage.xaml: 0 heading, 1 contract 7, 2 not contract 7 (dropped),
        // 3 contract 99 (dropped), 4 not contract 99, 5 border, 6 border, 7 new experience, 8 legacy (dropped), 9 button.
        var live = new List<string> { "heading", "c7", "c99n", "border1", "border2", "newExperience", "button" };
        var map = new ChildIndexMap([2, 3, 8]);

        // Typed on the line after "Contract 99 is not present.": a dropped element at 5, then a kept one at 6.
        map.InsertHole(5, live.Count);
        live.Insert(map.InsertItem(6, live.Count), "inserted");

        CollectionAssert.AreEqual(new[] { "heading", "c7", "c99n", "inserted", "border1", "border2", "newExperience", "button" }, live);
        CollectionAssert.AreEqual(new[] { 2, 3, 5, 10 }, Holes(map));
    }

    [TestMethod]
    public void BothConsistentOrdersOfTwoInsertionsGiveTheSameResult()
    {
        // The dropped element first, at 5, then the kept one at 6.
        var first = new List<string> { "a", "b", "c" };
        var firstMap = new ChildIndexMap([1, 2]);
        firstMap.InsertHole(4, first.Count);
        first.Insert(firstMap.InsertItem(5, first.Count), "new");

        // The kept element first, at 4, then the dropped one before it, at 4 again.
        var second = new List<string> { "a", "b", "c" };
        var secondMap = new ChildIndexMap([1, 2]);
        second.Insert(secondMap.InsertItem(4, second.Count), "new");
        secondMap.InsertHole(4, second.Count);

        CollectionAssert.AreEqual(first, second);
        CollectionAssert.AreEqual(Holes(firstMap), Holes(secondMap));
        CollectionAssert.AreEqual(new[] { "a", "b", "new", "c" }, first);
    }

    [TestMethod]
    public void MovingAnObjectOverHolesRemovesAndAddsIt()
    {
        // Model: a H b H c; c moves to the front.
        var live = new List<string> { "a", "b", "c" };
        var map = new ChildIndexMap([1, 3]);

        ChildSlot removed = map.RemoveAt(4, live.Count);
        string moved = live[removed.LiveIndex];
        live.RemoveAt(removed.LiveIndex);
        live.Insert(map.InsertItem(0, live.Count), moved);

        CollectionAssert.AreEqual(new[] { "c", "a", "b" }, live);
        CollectionAssert.AreEqual(new[] { 2, 4 }, Holes(map));
    }

    [TestMethod]
    public void ReplacingADroppedElementWithAKeptOneShowsItInItsPlace()
    {
        // Model: a H b; the prefix of the dropped element is changed to one that holds.
        var live = new List<string> { "a", "b" };
        var map = new ChildIndexMap([1]);

        Assert.AreEqual(ChildSlot.Hole, map.RemoveAt(1, live.Count));
        live.Insert(map.InsertItem(1, live.Count), "kept");

        CollectionAssert.AreEqual(new[] { "a", "kept", "b" }, live);
        Assert.IsEmpty(map.Holes);
    }

    [TestMethod]
    public void ReplacingAKeptElementWithADroppedOneRemovesItFromView()
    {
        // Model: a b c; the prefix of b is changed to one that does not hold.
        var live = new List<string> { "a", "b", "c" };
        var map = new ChildIndexMap();

        ChildSlot removed = map.RemoveAt(1, live.Count);
        live.RemoveAt(removed.LiveIndex);
        map.InsertHole(1, live.Count);

        CollectionAssert.AreEqual(new[] { "a", "c" }, live);
        CollectionAssert.AreEqual(new[] { 1 }, Holes(map));
        Assert.AreEqual(ChildSlot.Live(1), map.Find(2, live.Count));
    }

    [TestMethod]
    public void UndoAndRedoOfAnInsertionNextToHolesRestoreTheState()
    {
        var live = new List<string> { "a", "b", "c" };
        var map = new ChildIndexMap([1, 3]);

        live.Insert(map.InsertItem(4, live.Count), "d");
        CollectionAssert.AreEqual(new[] { "a", "b", "d", "c" }, live);

        ChildSlot undone = map.RemoveAt(4, live.Count);
        live.RemoveAt(undone.LiveIndex);
        CollectionAssert.AreEqual(new[] { "a", "b", "c" }, live);
        CollectionAssert.AreEqual(new[] { 1, 3 }, Holes(map));

        live.Insert(map.InsertItem(4, live.Count), "d");
        CollectionAssert.AreEqual(new[] { "a", "b", "d", "c" }, live);
    }

    [TestMethod]
    public void TheMapAgreesWithAModelOfTheWholeCollectionForRandomEdits()
    {
        // The reference keeps the model's view of the collection, holes included, and applies every operation to it
        // literally. The map only knows the holes; together with the live collection it must give the same answers.
        var random = new Random(20261115);
        for (int run = 0; run < 400; run++)
        {
            var model = new List<(bool IsHole, int Id)>();
            var live = new List<int>();
            var map = new ChildIndexMap();
            int nextId = 0;

            for (int step = 0; step < 60; step++)
            {
                int count = model.Count;
                int index = random.Next(-2, count + 3);
                switch (random.Next(4))
                {
                    case 0:
                        {
                            int position = index < 0 || index > count ? count : index;
                            model.Insert(position, (true, -1));
                            Assert.AreEqual(position, map.InsertHole(index, live.Count), $"run {run} step {step}: hole at {index}");
                            break;
                        }

                    case 1:
                        {
                            int position = index < 0 || index > count ? count : index;
                            int id = nextId++;
                            model.Insert(position, (false, id));
                            live.Insert(map.InsertItem(index, live.Count), id);
                            break;
                        }

                    case 2:
                        {
                            ChildSlot slot = map.RemoveAt(index, live.Count);
                            if (index < 0 || index >= count)
                            {
                                Assert.AreEqual(ChildSlot.Outside, slot, $"run {run} step {step}: remove {index}");
                                break;
                            }

                            (bool isHole, int id) = model[index];
                            model.RemoveAt(index);
                            if (isHole)
                            {
                                Assert.AreEqual(ChildSlot.Hole, slot, $"run {run} step {step}: remove {index}");
                            }
                            else
                            {
                                Assert.AreEqual(ChildSlotKind.Live, slot.Kind, $"run {run} step {step}: remove {index}");
                                Assert.AreEqual(id, live[slot.LiveIndex], $"run {run} step {step}: remove {index}");
                                live.RemoveAt(slot.LiveIndex);
                            }

                            break;
                        }

                    default:
                        {
                            ChildSlot slot = map.Find(index, live.Count);
                            if (index < 0 || index >= count)
                            {
                                Assert.AreEqual(ChildSlot.Outside, slot, $"run {run} step {step}: find {index}");
                            }
                            else if (model[index].IsHole)
                            {
                                Assert.AreEqual(ChildSlot.Hole, slot, $"run {run} step {step}: find {index}");
                            }
                            else
                            {
                                Assert.AreEqual(model[index].Id, live[slot.LiveIndex], $"run {run} step {step}: find {index}");
                            }

                            break;
                        }
                }

                CollectionAssert.AreEqual(
                    model.Where(entry => !entry.IsHole).Select(entry => entry.Id).ToArray(),
                    live.ToArray(),
                    $"run {run} step {step}: live objects"
                );
                CollectionAssert.AreEqual(
                    model.Select((entry, position) => (entry.IsHole, position)).Where(entry => entry.IsHole).Select(entry => entry.position).ToArray(),
                    Holes(map),
                    $"run {run} step {step}: holes"
                );
            }
        }
    }

    [TestMethod]
    public void MapsAreKeptPerCollectionObjectNotPerContent()
    {
        var maps = new ChildIndexMaps();
        var first = new List<string> { "a" };
        var second = new List<string> { "a" };

        ChildIndexMap created = maps.InsertHole(first, 0, first.Count);
        ChildIndexMap again = maps.InsertHole(first, 2, first.Count);

        Assert.AreSame(created, again, "The first dropped element creates the map; later ones use it.");
        Assert.IsTrue(maps.TryGet(first, out ChildIndexMap? map));
        CollectionAssert.AreEqual(new[] { 0, 2 }, Holes(map));
        Assert.IsFalse(maps.TryGet(second, out _), "An equal collection is another collection.");
        Assert.AreEqual(1, maps.Count);
    }

    [TestMethod]
    public void RegisteringReplacesTheHolesAndResettingOrReleasingForgetsThem()
    {
        var maps = new ChildIndexMaps();
        var collection = new List<string> { "a", "b" };
        var other = new List<string>();

        maps.InsertHole(collection, 0, collection.Count);
        maps.Register(collection, [1, 2]);
        maps.Register(other, [0]);

        Assert.IsTrue(maps.TryGet(collection, out ChildIndexMap? map));
        CollectionAssert.AreEqual(new[] { 1, 2 }, Holes(map));
        Assert.AreEqual(2, maps.Count);

        Assert.IsTrue(maps.Reset(collection));
        Assert.IsFalse(maps.TryGet(collection, out _));

        maps.Release([other, new object()]);
        Assert.AreEqual(0, maps.Count);
    }

    private static int[] Holes(ChildIndexMap map) => map.Holes.ToArray();
}
