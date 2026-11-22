// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinUIDesigner.Surface.Runtime;

[TestClass]
public sealed class RenderStallReportsTests
{
    // Stands for a surface: the reports tell surfaces apart by identity, as a reloaded document gets a new one.
    private sealed class Surface
    {
    }

    [TestMethod]
    public void NothingIsToldWhileTheFrameworkDraws()
    {
        var reports = new RenderStallReports();

        Assert.IsFalse(reports.HasStalled);
        Assert.IsFalse(reports.ShouldTell(new Surface()));
    }

    [TestMethod]
    public void EverySurfaceIsToldOnceAfterTheFrameworkHasStopped()
    {
        var reports = new RenderStallReports();
        var shown = new Surface();
        var openedLater = new Surface();
        reports.Stalled("stopped", "details");

        Assert.IsTrue(reports.ShouldTell(shown));
        Assert.IsFalse(reports.ShouldTell(shown));
        Assert.IsTrue(reports.ShouldTell(openedLater));
        Assert.IsFalse(reports.ShouldTell(openedLater));
    }

    [TestMethod]
    public void ADocumentThatIsLoadedAgainIsToldAgain()
    {
        // "Reload the designer" creates the surface of the document anew, in the same process.
        var reports = new RenderStallReports();
        reports.Stalled("stopped", "details");

        Assert.IsTrue(reports.ShouldTell(new Surface()));
        Assert.IsTrue(reports.ShouldTell(new Surface()));
    }

    [TestMethod]
    public void TheReportKeepsItsTextAndTheErrorAfterWhichTheFrameworkStopped()
    {
        var reports = new RenderStallReports();
        reports.Stalled("stopped", "System.Runtime.InteropServices.COMException (0x80004005): Unspecified error");

        Assert.IsTrue(reports.HasStalled);
        Assert.AreEqual("stopped", reports.Message);
        Assert.AreEqual("System.Runtime.InteropServices.COMException (0x80004005): Unspecified error", reports.Details);
    }
}
