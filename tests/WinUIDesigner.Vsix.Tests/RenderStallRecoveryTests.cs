// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinUIDesigner.Recovery;

[TestClass]
public sealed class RenderStallRecoveryTests
{
    private static readonly DateTime _Start = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);

    [TestMethod]
    public void TheReportIsRecognizedByItsText() => Assert.IsTrue(RenderStallReport.IsReport(RenderStallReport.Message));

    [TestMethod]
    public void TheReportIsRecognizedWithSpacesAroundIt()
    {
        // Visual Studio trims the message of an unhandled exception before it keeps it; the text may arrive padded.
        Assert.IsTrue(RenderStallReport.IsReport($"  {RenderStallReport.Message}\r\n"));
    }

    [TestMethod]
    public void OtherUnhandledExceptionsAreNotTheReport()
    {
        Assert.IsFalse(RenderStallReport.IsReport(null));
        Assert.IsFalse(RenderStallReport.IsReport(string.Empty));
        Assert.IsFalse(RenderStallReport.IsReport("The surface process crashed."));
        Assert.IsFalse(RenderStallReport.IsReport($"{RenderStallReport.Message} Something else."));
    }

    [TestMethod]
    public void TheReportOfAStallAtTheFirstFrameIsRecognized()
    {
        Assert.IsTrue(RenderStallReport.IsReport(RenderStallReport.FirstFrameMessage));
        Assert.IsTrue(RenderStallReport.IsFirstFrameReport(RenderStallReport.FirstFrameMessage));
        Assert.IsFalse(RenderStallReport.IsFirstFrameReport(RenderStallReport.Message));
        Assert.IsFalse(RenderStallReport.IsFirstFrameReport(null));
    }

    [TestMethod]
    public void TheReportOfAStallAtTheFirstFrameDoesNotAdviseAReload()
    {
        // Every new process stops at the same frame: a reload does not help, and the text must not say it does.
        Assert.AreEqual(RenderStallReport.FirstFrameMessage, RenderStallReport.MessageFor(stoppedAtFirstFrame: true));
        Assert.AreEqual(RenderStallReport.Message, RenderStallReport.MessageFor(stoppedAtFirstFrame: false));
        Assert.DoesNotContain("Reload the designer", RenderStallReport.FirstFrameMessage);
    }

    [TestMethod]
    public void TheContextOfTheSurfaceTravelsOnTheFirstLineOfTheCallStack()
    {
        string callStack = RenderStallReport.WithContext("3F2A9C1B7E4D5A60", "System.Runtime.InteropServices.COMException (0x80004005): Unspecified error");

        Assert.AreEqual("3F2A9C1B7E4D5A60", RenderStallReport.ReadContext(callStack));
        Assert.AreEqual("3F2A9C1B7E4D5A60", RenderStallReport.ReadContext(RenderStallReport.WithContext("3F2A9C1B7E4D5A60", string.Empty)));
    }

    [TestMethod]
    public void ACallStackWithoutAContextNamesNone()
    {
        Assert.IsNull(RenderStallReport.ReadContext(null));
        Assert.IsNull(RenderStallReport.ReadContext(string.Empty));
        Assert.IsNull(RenderStallReport.ReadContext("System.Runtime.InteropServices.COMException (0x80004005): Unspecified error"));
        Assert.IsNull(RenderStallReport.ReadContext(RenderStallReport.WithContext(" ", "details")));
    }

    [TestMethod]
    public void TheLimiterAllowsTheMaximumInTheWindowAndRefusesTheNext()
    {
        RestartLimiter limiter = new(3, TimeSpan.FromMinutes(2));

        Assert.IsTrue(limiter.TryAcquire(_Start));
        Assert.IsTrue(limiter.TryAcquire(_Start.AddSeconds(10)));
        Assert.IsTrue(limiter.TryAcquire(_Start.AddSeconds(20)));
        Assert.IsFalse(limiter.TryAcquire(_Start.AddSeconds(30)));
    }

    [TestMethod]
    public void ARefusedRestartIsNotCounted()
    {
        RestartLimiter limiter = new(1, TimeSpan.FromMinutes(1));

        Assert.IsTrue(limiter.TryAcquire(_Start));
        Assert.IsFalse(limiter.TryAcquire(_Start.AddSeconds(5)));
        Assert.IsFalse(limiter.TryAcquire(_Start.AddSeconds(50)));

        // The window is counted from the restart that was allowed, not from the ones that were refused.
        Assert.IsTrue(limiter.TryAcquire(_Start.AddSeconds(60)));
    }

    [TestMethod]
    public void TheLimiterAllowsAgainWhenTheOldestRestartHasLeftTheWindow()
    {
        RestartLimiter limiter = new(2, TimeSpan.FromMinutes(2));

        Assert.IsTrue(limiter.TryAcquire(_Start));
        Assert.IsTrue(limiter.TryAcquire(_Start.AddSeconds(60)));
        Assert.IsFalse(limiter.TryAcquire(_Start.AddSeconds(119)));

        // Exactly one window after the first one the first one no longer counts.
        Assert.IsTrue(limiter.TryAcquire(_Start.AddSeconds(120)));
        Assert.IsFalse(limiter.TryAcquire(_Start.AddSeconds(121)));
    }

    [TestMethod]
    public void TheLimiterCountsTheRestartsInTheWindow()
    {
        RestartLimiter limiter = new(5, TimeSpan.FromMinutes(1));

        _ = limiter.TryAcquire(_Start);
        _ = limiter.TryAcquire(_Start.AddSeconds(30));

        Assert.AreEqual(2, limiter.Count(_Start.AddSeconds(40)));
        Assert.AreEqual(1, limiter.Count(_Start.AddSeconds(70)));
        Assert.AreEqual(0, limiter.Count(_Start.AddSeconds(200)));
    }

    [TestMethod]
    public void TheLimiterNeedsAPositiveMaximumAndWindow()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new RestartLimiter(0, TimeSpan.FromMinutes(1)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new RestartLimiter(1, TimeSpan.Zero));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new RestartLimiter(1, TimeSpan.FromSeconds(-1)));
    }
}
