// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using WinUIDesigner.Surface.Common;

[TestClass]
public sealed class StyleEditTests
{
    private sealed record Insertion(List<string> Collection, int Index, string Item);

    private sealed class Holder(object? value, bool accepts = true, bool fails = false)
    {
        public object? Value { get; private set; } = value;

        public bool Accepts { get; } = accepts;

        public void Take(object newValue)
        {
            if (fails)
            {
                throw new InvalidOperationException("WinUI rejected the value.");
            }

            Value = newValue;
        }
    }

    // Stand-ins for the control types that the TargetType rules compare.
    private class Control { }

    private class ButtonBase : Control { }

    private sealed class Button : ButtonBase { }

    private sealed class ToggleButton : ButtonBase { }

    [TestMethod]
    public void PendingInsertionsAreTakenPerCollectionInTheirOriginalOrder()
    {
        var first = new List<string>();
        var second = new List<string>();
        var queue = new DeferredInsertionQueue<Insertion>(insertion => insertion.Collection);
        queue.Add(new(first, 0, "a"));
        queue.Add(new(second, 0, "x"));
        queue.Add(new(first, 1, "b"));

        CollectionAssert.AreEqual(new[] { "a", "b" }, queue.TakeFor(first).Select(insertion => insertion.Item).ToArray());
        Assert.AreEqual(1, queue.Count);
        Assert.AreEqual(0, queue.TakeFor(first).Count);
        CollectionAssert.AreEqual(new[] { "x" }, queue.TakeAll().Select(insertion => insertion.Item).ToArray());
        Assert.AreEqual(0, queue.Count);
    }

    [TestMethod]
    public void ReplacingTwoSettersInOneBatchKeepsTheIndicesVisualStudioComputed()
    {
        // Visual Studio replaces each changed setter by a new one: remove at i, add at i. Every index assumes
        // the earlier actions of the batch already ran. SurfaceService queues each AddChild of a setter, runs
        // RemoveChild, GetChild and ClearCollectionProperty through RunAfterPendingInsertions, and inserts the
        // rest after the batch; the actions themselves need a WinUI surface, so this test runs the queue on a list.
        var setters = new List<string> { "FontSize=20", "Padding=7" };
        var queue = new DeferredInsertionQueue<Insertion>(insertion => insertion.Collection);

        queue.RunAfterPendingInsertions(setters, Insert, () => setters.RemoveAt(0));
        queue.Add(new(setters, 0, "FontSize=24"));
        queue.RunAfterPendingInsertions(setters, Insert, () => setters.RemoveAt(1));
        queue.Add(new(setters, 1, "Padding=9"));
        InsertPending(queue.TakeAll());

        CollectionAssert.AreEqual(new[] { "FontSize=24", "Padding=9" }, setters);
    }

    [TestMethod]
    public void AnOperationOnAnotherCollectionLeavesThePendingInsertionsWaiting()
    {
        var setters = new List<string>();
        var children = new List<string> { "Border" };
        var queue = new DeferredInsertionQueue<Insertion>(insertion => insertion.Collection);
        queue.Add(new(setters, 0, "FontSize=24"));

        queue.RunAfterPendingInsertions(children, Insert, () => children.RemoveAt(0));
        queue.RunAfterPendingInsertions(null, Insert, () => { });

        Assert.IsEmpty(children);
        Assert.IsEmpty(setters, "A setter is inserted when its own collection is used, or after the batch.");
        Assert.AreEqual(1, queue.Count);
    }

    [TestMethod]
    [DataRow(false, true, false, true, DisplayName = "The setters of a visual state that can be active")]
    [DataRow(false, true, true, false, DisplayName = "The setters of a visual state of a new document or a lookup")]
    [DataRow(true, true, true, true, DisplayName = "A sealed setter collection of a visual state")]
    [DataRow(true, false, true, true, DisplayName = "The setters of a style in use, also while a document is built")]
    [DataRow(false, false, false, false, DisplayName = "The setters of a style that is not in use")]
    public void OnlyEditsThatThePublicApiCannotApplyGoThroughXamlDiagnostics(bool isSealed, bool ownerIsVisualState, bool buildsDetachedObjects, bool expected)
    {
        Assert.AreEqual(expected, SetterEditRoute.UsesXamlDiagnostics(isSealed, ownerIsVisualState, buildsDetachedObjects));
    }

    [TestMethod]
    public void RemovingAndAddingAStyleUnderOneKeyIsAReplacement()
    {
        var tracker = new ReplacedValueTracker();
        object dictionary = new();
        object oldStyle = new();
        object newStyle = new();

        tracker.Removed(dictionary, "ProbeStyle", oldStyle);
        tracker.Added(dictionary, "ProbeStyle", newStyle);

        var replacement = tracker.TakeReplacements().Single();
        Assert.AreSame(dictionary, replacement.Dictionary);
        Assert.AreSame(oldStyle, replacement.OldValue);
        Assert.AreSame(newStyle, replacement.NewValue);
        Assert.AreEqual(0, tracker.TakeReplacements().Count);
    }

    [TestMethod]
    public void ImplicitStyleKeysPairByType()
    {
        var tracker = new ReplacedValueTracker();
        object dictionary = new();
        object newStyle = new();

        tracker.Removed(dictionary, typeof(Uri), new object());
        tracker.Added(dictionary, typeof(string), newStyle);
        Assert.AreEqual(0, tracker.TakeReplacements().Count, "Another type is another key.");

        tracker.Removed(dictionary, typeof(Uri), new object());
        tracker.Added(dictionary, typeof(Uri), newStyle);
        Assert.AreSame(newStyle, tracker.TakeReplacements().Single().NewValue);
    }

    [TestMethod]
    public void AnAdditionWithoutARemovalIsNotAReplacement()
    {
        var tracker = new ReplacedValueTracker();
        object dictionary = new();

        tracker.Removed(dictionary, "ProbeStyle", new object());
        tracker.Added(new object(), "ProbeStyle", new object());
        tracker.Added(dictionary, "OtherStyle", new object());

        Assert.AreEqual(0, tracker.TakeReplacements().Count, "Neither addition matches the removed key of that dictionary.");
    }

    [TestMethod]
    public void ReaddingTheSameInstanceIsNotAReplacement()
    {
        var tracker = new ReplacedValueTracker();
        object dictionary = new();
        object style = new();

        tracker.Removed(dictionary, "ProbeStyle", style);
        tracker.Added(dictionary, "ProbeStyle", style);

        Assert.AreEqual(0, tracker.TakeReplacements().Count);
    }

    [TestMethod]
    public void ARemovalDoesNotPairWithAnAdditionInALaterBatch()
    {
        var tracker = new ReplacedValueTracker();
        object dictionary = new();
        tracker.Removed(dictionary, "ProbeStyle", new object());
        tracker.TakeReplacements();

        tracker.Added(dictionary, "ProbeStyle", new object());

        Assert.AreEqual(0, tracker.TakeReplacements().Count);
    }

    [TestMethod]
    public void EachRemovalThroughXamlDiagnosticsPairsWithOneAddition()
    {
        var keys = new DetachedKeyCounter();
        keys.Removed("Heading");

        Assert.IsTrue(keys.TryPairAddition("Heading"), "The addition after the removal resolves the references it detached.");
        Assert.IsFalse(keys.TryPairAddition("Heading"), "A later addition under the key, in any dictionary, keeps the public API.");
        Assert.IsFalse(keys.TryPairAddition("Subheading"));
    }

    [TestMethod]
    public void EveryRemovalOfAKeyPairsWithAnAdditionOfItsOwn()
    {
        var keys = new DetachedKeyCounter();
        keys.Removed(typeof(Uri));
        keys.Removed(typeof(Uri));

        Assert.IsFalse(keys.TryPairAddition(typeof(string)), "Another type is another key.");
        Assert.IsTrue(keys.TryPairAddition(typeof(Uri)));
        Assert.IsTrue(keys.TryPairAddition(typeof(Uri)));
        Assert.IsFalse(keys.TryPairAddition(typeof(Uri)));
    }

    [TestMethod]
    public void AReferenceThatCannotMoveKeepsTheOldValueAndTheOthersStillMove()
    {
        object oldValue = new();
        object newValue = new();
        object otherValue = new();
        var moved = new Holder(oldValue);
        var failing = new Holder(oldValue, fails: true);
        var rejecting = new Holder(oldValue, accepts: false);
        var unrelated = new Holder(otherValue);
        var last = new Holder(oldValue);
        var failures = new List<Exception>();

        CreateMover(failures).MoveAll([moved, failing, rejecting, unrelated, last], oldValue, newValue);

        Assert.AreSame(newValue, moved.Value);
        Assert.AreSame(oldValue, failing.Value);
        Assert.AreSame(oldValue, rejecting.Value, "A holder that WinUI would reject keeps the old value, without a failure.");
        Assert.AreSame(otherValue, unrelated.Value);
        Assert.AreSame(newValue, last.Value, "The holders after a failure still move.");
        Assert.HasCount(1, failures);
    }

    [TestMethod]
    public void HoldersThatCannotBeListedEndTheirListWithoutFailingTheMove()
    {
        object oldValue = new();
        object newValue = new();
        var first = new Holder(oldValue);
        var failures = new List<Exception>();

        IEnumerable<Holder> Holders()
        {
            yield return first;
            throw new InvalidOperationException("A deferred resource could not be created.");
        }

        CreateMover(failures).MoveAll(Holders(), oldValue, newValue);

        Assert.AreSame(newValue, first.Value);
        Assert.HasCount(1, failures);
    }

    [TestMethod]
    public void AnElementCanUseAStyleForItsTypeOrABaseType()
    {
        var button = new Button();

        Assert.IsTrue(StyleReplacementRules.CanStyle(typeof(Button), button));
        Assert.IsTrue(StyleReplacementRules.CanStyle(typeof(ButtonBase), button));
        Assert.IsFalse(StyleReplacementRules.CanStyle(typeof(ToggleButton), button), "An element keeps its style when the rebuilt one targets another type.");
        Assert.IsTrue(StyleReplacementRules.CanStyle(null, button), "WinUI decides about a style without a TargetType.");
    }

    [TestMethod]
    public void AStyleCanBeBasedOnAStyleForItsTypeOrABaseType()
    {
        Assert.IsTrue(StyleReplacementRules.CanBaseOn(typeof(Button), typeof(Button)));
        Assert.IsTrue(StyleReplacementRules.CanBaseOn(typeof(Button), typeof(Control)));
        Assert.IsFalse(StyleReplacementRules.CanBaseOn(typeof(Button), typeof(ToggleButton)));
        Assert.IsFalse(StyleReplacementRules.CanBaseOn(typeof(ButtonBase), typeof(Button)), "A base cannot target a derived type.");
        Assert.IsTrue(StyleReplacementRules.CanBaseOn(null, typeof(Button)));
        Assert.IsTrue(StyleReplacementRules.CanBaseOn(typeof(Button), null));
    }

    [TestMethod]
    public void DerivedStylesAreNotSoughtInTheDictionariesOfOtherFiles()
    {
        Assert.IsTrue(StyleReplacementRules.SearchesDictionary(isFrameworkResources: false, source: null));
        Assert.IsFalse(StyleReplacementRules.SearchesDictionary(isFrameworkResources: false, new Uri("ms-appx:///SampleLibrary/Themes/Generic.xaml")), "A library theme holds thousands of deferred resources.");
        Assert.IsFalse(StyleReplacementRules.SearchesDictionary(isFrameworkResources: true, source: null));
    }

    [TestMethod]
    [DataRow("BasedOn:Microsoft.UI.Xaml.Style, Microsoft.WinUI, Version=3.0.0.0, Culture=neutral, PublicKeyToken=null", "Microsoft.UI.Xaml.Style.BasedOn")]
    [DataRow("Value:Microsoft.UI.Xaml.Setter", "Microsoft.UI.Xaml.Setter.Value")]
    [DataRow("Value", "Microsoft.UI.Xaml.Setter.Value")]
    [DataRow("Property:Windows.UI.Xaml.Setter, Windows.Foundation.UniversalApiContract, Version=10.0.0.0, ContentType=WindowsRuntime", "Microsoft.UI.Xaml.Setter.Property")]
    [DataRow("Items:Sample.Control`1[[System.String, System.Runtime]], Sample", "Sample.Control`1[[System.String, System.Runtime]].Items")]
    public void ActionPropertyNamesBecomeFullMetadataNames(string actionPropertyName, string expected)
    {
        Assert.AreEqual(expected, XamlDiagnosticsPropertyName.FromActionProperty(actionPropertyName, "Microsoft.UI.Xaml.Setter"));
    }

    private static void InsertPending(List<Insertion> insertions)
    {
        foreach (Insertion insertion in insertions)
        {
            Insert(insertion);
        }
    }

    private static void Insert(Insertion insertion)
    {
        insertion.Collection.Insert(Math.Min(insertion.Index, insertion.Collection.Count), insertion.Item);
    }

    private static ReferenceMover<Holder, object> CreateMover(List<Exception> failures)
    {
        return new(
            static holder => holder.Value,
            static (holder, _) => holder.Accepts,
            static (holder, value) => holder.Take(value),
            failures.Add
        );
    }
}
