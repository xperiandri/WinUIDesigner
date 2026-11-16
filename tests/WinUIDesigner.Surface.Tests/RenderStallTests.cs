// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinUIDesigner.Surface.Runtime;

[TestClass]
public sealed class RenderStallTests
{
    [TestMethod]
    public void NoFramesIsAStall()
    {
        Assert.IsTrue(RenderStall.IsStalled(1000, 1000));
    }

    [TestMethod]
    public void FewerFramesThanTheMinimumIsAStall()
    {
        Assert.IsTrue(RenderStall.IsStalled(1000, 1000 + RenderStall.MinimumFrames - 1));
    }

    [TestMethod]
    public void TheMinimumNumberOfFramesIsNotAStall()
    {
        Assert.IsFalse(RenderStall.IsStalled(1000, 1000 + RenderStall.MinimumFrames));
    }

    [TestMethod]
    public void ASurfaceThatDrawsAtSixtyFramesInASecondIsNotStalled()
    {
        Assert.IsFalse(RenderStall.IsStalled(2000, 2000 + 3 * 60));
    }

    [TestMethod]
    public void TheCounterMayWrapAroundWhileTheSurfaceDraws()
    {
        Assert.IsFalse(RenderStall.IsStalled(int.MaxValue - 10, int.MinValue + 100));
    }

    [TestMethod]
    public void TheCounterMayWrapAroundWhileTheSurfaceHasStopped()
    {
        Assert.IsTrue(RenderStall.IsStalled(int.MaxValue, int.MaxValue));
    }
}
