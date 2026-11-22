// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinUIDesigner.Surface.Runtime;

[TestClass]
public sealed class FreezeRecoveryTests
{
    [TestMethod]
    [DataRow(true, true)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(false, false)]
    public void ASurfaceThatIsGoneDoesNothingWhenTheTimerFires(bool frozen, bool renderingSuspended)
    {
        Assert.AreEqual(FreezeTimerStep.Nothing, FreezeRecovery.OnTimer(disposed: true, frozen, renderingSuspended));
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void ASurfaceThatIsStillFrozenUnfreezesItself(bool renderingSuspended)
    {
        Assert.AreEqual(FreezeTimerStep.Unfreeze, FreezeRecovery.OnTimer(disposed: false, frozen: true, renderingSuspended));
    }

    [TestMethod]
    public void AnUnfrozenSurfaceWhoseRenderingIsStillSuspendedResumesIt()
    {
        Assert.AreEqual(FreezeTimerStep.ResumeRendering, FreezeRecovery.OnTimer(disposed: false, frozen: false, renderingSuspended: true));
    }

    [TestMethod]
    public void AnUnfrozenSurfaceThatDrawsDoesNothing()
    {
        Assert.AreEqual(FreezeTimerStep.Nothing, FreezeRecovery.OnTimer(disposed: false, frozen: false, renderingSuspended: false));
    }

    [TestMethod]
    public void AResumptionThatFailsIsTriedAgainEveryHalfSecondTwentyTimes()
    {
        Assert.AreEqual(TimeSpan.FromMilliseconds(500), FreezeRecovery.ResumeRetryInterval);
        Assert.IsTrue(FreezeRecovery.ShouldRetryResume(1));
        Assert.IsTrue(FreezeRecovery.ShouldRetryResume(19));
        Assert.IsFalse(FreezeRecovery.ShouldRetryResume(20));
    }

    [TestMethod]
    public void ASurfaceUnfreezesItselfAfterTenSeconds() => Assert.AreEqual(TimeSpan.FromSeconds(10), FreezeRecovery.Timeout);

    [TestMethod]
    public void RenderingIsResumedOnlyWhileNoSurfaceIsFrozen()
    {
        Assert.IsTrue(FreezeRecovery.MayResumeRendering([]));
        Assert.IsTrue(FreezeRecovery.MayResumeRendering([false, false]));
        Assert.IsFalse(FreezeRecovery.MayResumeRendering([false, true]));
    }
}
