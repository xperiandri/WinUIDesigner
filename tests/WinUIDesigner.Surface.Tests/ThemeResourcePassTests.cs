// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using WinUIDesigner.Surface.Common;

/// <summary>
/// The pass over the <c>{ThemeResource}</c> properties that the surface runs after new artboard colours, and the layout
/// that follows it. The response of the colours stays a success when that layout fails.
/// </summary>
[TestClass]
public sealed class ThemeResourcePassTests
{
    [TestMethod]
    public void AChangedPropertyIsFollowedByTheCompletion()
    {
        var steps = new List<string>();

        bool changed = ThemeResourcePass.ReapplyThenComplete(
            () =>
            {
                steps.Add("reapply");
                return true;
            },
            () => steps.Add("complete"),
            _ => steps.Add("failure")
        );

        Assert.IsTrue(changed);
        CollectionAssert.AreEqual(new[] { "reapply", "complete" }, steps);
    }

    [TestMethod]
    public void APassThatChangedNothingLeavesTheLayoutAlone()
    {
        // The artboard colours that Visual Studio sends again with every new surface change no property.
        var steps = new List<string>();

        bool changed = ThemeResourcePass.ReapplyThenComplete(
            () =>
            {
                steps.Add("reapply");
                return false;
            },
            () => steps.Add("complete"),
            _ => steps.Add("failure")
        );

        Assert.IsFalse(changed);
        CollectionAssert.AreEqual(new[] { "reapply" }, steps);
    }

    [TestMethod]
    public void ACompletionThatFailsIsReportedAndDoesNotFailTheRequest()
    {
        // The new values are in place when the layout fails after them.
        var cause = new InvalidOperationException("The layout failed.");
        var failures = new List<Exception>();

        bool changed = ThemeResourcePass.ReapplyThenComplete(() => true, () => throw cause, failures.Add);

        Assert.IsTrue(changed);
        Assert.AreSame(cause, Assert.ContainsSingle(failures));
    }

    [TestMethod]
    public void ACompletionThatSucceedsIsNotReported()
    {
        var failures = new List<Exception>();

        ThemeResourcePass.ReapplyThenComplete(() => true, () => { }, failures.Add);

        Assert.IsEmpty(failures);
    }
}
