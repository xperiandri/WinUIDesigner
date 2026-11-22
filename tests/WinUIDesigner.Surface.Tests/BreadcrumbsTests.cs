// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinUIDesigner.Surface.Runtime;

[TestClass]
public sealed class BreadcrumbsTests
{
    private static string Text(string entry) => entry[(entry.IndexOf(' ') + 1)..];

    [TestMethod]
    public void AFreshJournalIsEmpty()
    {
        Assert.AreEqual(0, new Breadcrumbs(4).Snapshot().Count);
    }

    [TestMethod]
    public void EntriesComeOutOldestFirst()
    {
        var breadcrumbs = new Breadcrumbs(4);
        breadcrumbs.Add("one");
        breadcrumbs.Add("two");
        breadcrumbs.Add("three");

        CollectionAssert.AreEqual(new[] { "one", "two", "three" }, breadcrumbs.Snapshot().Select(Text).ToArray());
    }

    [TestMethod]
    public void AFullJournalForgetsTheOldestEntries()
    {
        var breadcrumbs = new Breadcrumbs(3);
        foreach (string text in new[] { "a", "b", "c", "d", "e" })
        {
            breadcrumbs.Add(text);
        }

        CollectionAssert.AreEqual(new[] { "c", "d", "e" }, breadcrumbs.Snapshot().Select(Text).ToArray());
    }

    [TestMethod]
    public void EntriesCarryTheTimeTheyWereAdded()
    {
        var breadcrumbs = new Breadcrumbs(2);
        breadcrumbs.Add("event");

        StringAssert.Matches(breadcrumbs.Snapshot()[0], new System.Text.RegularExpressions.Regex(@"^\d\d:\d\d:\d\d\.\d{3} event$"));
    }

    [TestMethod]
    public void TheTimeOfAnEntryIsWrittenTheSameWayInEveryCulture()
    {
        // fi-FI and id-ID, for example, separate the hours, minutes and seconds with a dot.
        var dottedTime = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        dottedTime.DateTimeFormat.TimeSeparator = ".";
        CultureInfo culture = CultureInfo.CurrentCulture;
        var breadcrumbs = new Breadcrumbs(2);

        CultureInfo.CurrentCulture = dottedTime;
        try
        {
            breadcrumbs.Add("event");
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }

        StringAssert.Matches(breadcrumbs.Snapshot()[0], new Regex(@"^\d\d:\d\d:\d\d\.\d{3} event$"));
    }

    [TestMethod]
    public void ACapacityOfZeroIsRejected()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new Breadcrumbs(0));
    }

    [TestMethod]
    public void EntriesAddedFromManyThreadsAreAllKeptWithinTheCapacity()
    {
        var breadcrumbs = new Breadcrumbs(100);
        Parallel.For(0, 1000, index => breadcrumbs.Add("entry " + index));

        Assert.AreEqual(100, breadcrumbs.Snapshot().Count);
    }
}
