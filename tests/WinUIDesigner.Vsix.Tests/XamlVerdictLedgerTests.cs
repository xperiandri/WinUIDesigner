// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinUIDesigner.Platform;

/// <summary>
/// Covers which places of a document an edit puts on the other side of the line between what is built and what is dropped.
/// </summary>
[TestClass]
public sealed class XamlVerdictLedgerTests
{
    [TestMethod]
    public void FirstLookIsTheStateThePreviewWasBuiltFrom()
    {
        var ledger = new XamlVerdictLedger<string>();

        Assert.IsEmpty(ledger.Update(() => Places(("a", false), ("b", true)), hasPolicyNamespaces: true));
        Assert.IsEmpty(ledger.Update(() => Places(("a", false), ("b", true)), hasPolicyNamespaces: true));
    }

    [TestMethod]
    public void PlaceThatIsDroppedNowButWasNotIsReported()
    {
        // <TextBlock Text="A"/> became <c99:TextBlock Text="A"/>, and the document model noticed nothing.
        var ledger = new XamlVerdictLedger<string>();
        ledger.Update(() => Places(("textblock", false), ("border", false)), hasPolicyNamespaces: true);

        IReadOnlyList<string> changed = ledger.Update(() => Places(("textblock", true), ("border", false)), hasPolicyNamespaces: true);

        CollectionAssert.AreEqual(new[] { "textblock" }, changed.ToArray());
    }

    [TestMethod]
    public void PlaceThatIsNotDroppedAnymoreIsReported()
    {
        // The prefix of an element that was dropped when the document was loaded was taken away.
        var ledger = new XamlVerdictLedger<string>();
        ledger.Update(() => Places(("textblock", true)), hasPolicyNamespaces: true);

        IReadOnlyList<string> changed = ledger.Update(() => Places(("textblock", false)), hasPolicyNamespaces: true);

        CollectionAssert.AreEqual(new[] { "textblock" }, changed.ToArray());
    }

    [TestMethod]
    public void PlaceIsReportedOnceUntilItChangesAgain()
    {
        var ledger = new XamlVerdictLedger<string>();
        ledger.Update(() => Places(("a", false)), hasPolicyNamespaces: true);
        ledger.Update(() => Places(("a", true)), hasPolicyNamespaces: true);

        Assert.IsEmpty(ledger.Update(() => Places(("a", true)), hasPolicyNamespaces: true));
        Assert.HasCount(1, ledger.Update(() => Places(("a", false)), hasPolicyNamespaces: true));
    }

    [TestMethod]
    public void PlaceThatCameWithTheEditIsBuiltWithItsVerdict()
    {
        var ledger = new XamlVerdictLedger<string>();
        ledger.Update(() => Places(("a", false)), hasPolicyNamespaces: true);

        // An element that was inserted under a dropped prefix is built as dropped when it is inserted.
        Assert.IsEmpty(ledger.Update(() => Places(("a", false), ("inserted", true), ("kept", false)), hasPolicyNamespaces: true));
    }

    [TestMethod]
    public void PlaceThatWentIsNotReported()
    {
        var ledger = new XamlVerdictLedger<string>();
        ledger.Update(() => Places(("a", true), ("b", false)), hasPolicyNamespaces: true);

        Assert.IsEmpty(ledger.Update(() => Places(("b", false)), hasPolicyNamespaces: true));
    }

    [TestMethod]
    public void DocumentThatGainsPolicyNamespacesReportsWhatItDropsNow()
    {
        var ledger = new XamlVerdictLedger<string>();
        int looks = 0;

        // Nothing can be dropped in a document without a namespace that a policy applies to.
        Assert.IsEmpty(ledger.Update(() => { looks++; return Places(("a", false)); }, hasPolicyNamespaces: false));
        Assert.AreEqual(0, looks);

        // The declaration and its use came in one edit: which places were there before is not known.
        IReadOnlyList<string> changed = ledger.Update(() => Places(("a", true), ("b", false)), hasPolicyNamespaces: true);

        CollectionAssert.AreEqual(new[] { "a" }, changed.ToArray());
    }

    [TestMethod]
    public void DocumentThatLosesItsPolicyNamespacesReportsWhatWasDropped()
    {
        var ledger = new XamlVerdictLedger<string>();
        ledger.Update(() => Places(("a", true), ("b", false)), hasPolicyNamespaces: true);

        // The declaration was removed: what it dropped is built now.
        IReadOnlyList<string> changed = ledger.Update(() => Places(("a", false), ("b", false)), hasPolicyNamespaces: false);
        int looks = 0;
        IReadOnlyList<string> later = ledger.Update(() => { looks++; return Places(("a", false)); }, hasPolicyNamespaces: false);

        CollectionAssert.AreEqual(new[] { "a" }, changed.ToArray());
        Assert.IsEmpty(later);
        Assert.AreEqual(0, looks);
    }

    [TestMethod]
    public void LookIsNotMadeWhileNothingCanBeDropped()
    {
        var ledger = new XamlVerdictLedger<string>();
        int looks = 0;

        for (int index = 0; index < 3; index++)
        {
            Assert.IsEmpty(ledger.Update(() => { looks++; return Places(); }, hasPolicyNamespaces: false));
        }

        Assert.AreEqual(0, looks);
    }

    [TestMethod]
    public void ResetMakesTheNextLookAFirstOne()
    {
        var ledger = new XamlVerdictLedger<string>();
        ledger.Update(() => Places(("a", false)), hasPolicyNamespaces: true);

        ledger.Reset();

        Assert.IsEmpty(ledger.Update(() => Places(("a", true)), hasPolicyNamespaces: true));
    }

    [TestMethod]
    public void LedgerKeepsACopyOfWhatItWasGiven()
    {
        var ledger = new XamlVerdictLedger<string>();
        Dictionary<string, bool> given = Places(("a", false));
        ledger.Update(() => given, hasPolicyNamespaces: true);

        // The caller reuses its dictionary for the next look.
        given["a"] = true;

        CollectionAssert.AreEqual(new[] { "a" }, ledger.Update(() => given, hasPolicyNamespaces: true).ToArray());
    }

    private static Dictionary<string, bool> Places(params (string Place, bool Dropped)[] places)
    {
        return places.ToDictionary(place => place.Place, place => place.Dropped, StringComparer.Ordinal);
    }
}
