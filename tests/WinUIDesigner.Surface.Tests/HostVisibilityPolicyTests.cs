// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using WinUIDesigner.Surface.Common;

[TestClass]
public sealed class HostVisibilityPolicyTests
{
    private const int Maximum = 3;

    [TestMethod]
    public void AgreeingFrameworkAndWindowNeedNothing()
    {
        Assert.AreEqual(HostVisibilityAction.Nothing, HostVisibilityPolicy.Decide(true, true, false, 0, Maximum));
    }

    [TestMethod]
    public void AWindowThatVisualStudioHidesIsLeftAlone()
    {
        // The framework is right: the island is hidden, and the show message comes with the show.
        Assert.AreEqual(HostVisibilityAction.Nothing, HostVisibilityPolicy.Decide(false, false, false, 0, Maximum));
    }

    [TestMethod]
    public void AMinimizedTopLevelWindowIsLeftAlone()
    {
        Assert.AreEqual(HostVisibilityAction.Nothing, HostVisibilityPolicy.Decide(false, true, true, 0, Maximum));
    }

    [TestMethod]
    public void AVisibleWindowThatTheFrameworkThinksIsHiddenIsShownAgain()
    {
        for (int attempts = 0; attempts < Maximum; attempts++)
        {
            Assert.AreEqual(HostVisibilityAction.ShowAgain, HostVisibilityPolicy.Decide(false, true, false, attempts, Maximum), $"after {attempts} attempt(s)");
        }
    }

    [TestMethod]
    public void ItGivesUpWhenShowingTheWindowAgainDoesNotHelp()
    {
        Assert.AreEqual(HostVisibilityAction.GiveUp, HostVisibilityPolicy.Decide(false, true, false, Maximum, Maximum));
        Assert.AreEqual(HostVisibilityAction.GiveUp, HostVisibilityPolicy.Decide(false, true, false, Maximum + 5, Maximum));
    }

    [TestMethod]
    public void GivingUpNeedsAWindowThatIsVisible()
    {
        // Out of attempts, but the window was hidden since: nothing to do until it is shown.
        Assert.AreEqual(HostVisibilityAction.Nothing, HostVisibilityPolicy.Decide(false, false, false, Maximum, Maximum));
    }
}
