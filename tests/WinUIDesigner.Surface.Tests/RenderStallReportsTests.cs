// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinUIDesigner.Surface.Runtime;

[TestClass]
public sealed class RenderStallReportsTests
{
    // Stands for a surface: the reports tell surfaces apart by identity, as a reloaded document gets a new one.
    private sealed class Surface
    {
    }

    // The surface is out of reach of the test once this returns, also in a debug build, which keeps locals alive.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference Tell(RenderStallReports reports)
    {
        var surface = new Surface();
        Assert.IsTrue(reports.ShouldTell(surface));

        return new WeakReference(surface);
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
    public void ASurfaceWhoseReportCouldNotBeSentIsToldAgain()
    {
        var reports = new RenderStallReports();
        var surface = new Surface();
        reports.Stalled("stopped", "details");
        Assert.IsTrue(reports.ShouldTell(surface));

        // The message failed: the next call tells the surface, once.
        reports.NotTold(surface);

        Assert.IsTrue(reports.ShouldTell(surface));
        Assert.IsFalse(reports.ShouldTell(surface));
    }

    [TestMethod]
    public void TakingBackTheTellOfOneSurfaceLeavesTheOthersTold()
    {
        var reports = new RenderStallReports();
        var failed = new Surface();
        var told = new Surface();
        reports.Stalled("stopped", "details");
        Assert.IsTrue(reports.ShouldTell(failed));
        Assert.IsTrue(reports.ShouldTell(told));

        reports.NotTold(failed);

        Assert.IsFalse(reports.ShouldTell(told));
        Assert.IsTrue(reports.ShouldTell(failed));
    }

    [TestMethod]
    public void ASurfaceThatWasToldIsNotKeptAlive()
    {
        // The process goes on after the stall, and closed documents must not pile up in it.
        var reports = new RenderStallReports();
        reports.Stalled("stopped", "details");
        WeakReference told = Tell(reports);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.IsFalse(told.IsAlive);
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
