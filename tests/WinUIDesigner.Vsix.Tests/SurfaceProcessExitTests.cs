// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Globalization;
using WinUIDesigner.DesignerHost;

[TestClass]
public sealed class SurfaceProcessExitTests
{
    [TestMethod]
    public void NormalExitIsNoFailure()
    {
        Assert.IsFalse(SurfaceProcessExit.IsFailure(0));
        Assert.IsFalse(SurfaceProcessExit.IsKilled(0));
    }

    [TestMethod]
    public void EndByProcessKillIsNoFailure()
    {
        // Visual Studio ends the surface it reloads or closes with Process.Kill, which leaves exit code -1.
        Assert.IsTrue(SurfaceProcessExit.IsKilled(-1));
        Assert.IsFalse(SurfaceProcessExit.IsFailure(-1));
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(unchecked((int)0xC0000005))] // Access violation.
    [DataRow(unchecked((int)0xC0000409))] // Fail fast.
    [DataRow(unchecked((int)0xE0434352))] // Unhandled .NET exception.
    public void OtherExitCodesAreFailures(int exitCode)
    {
        Assert.IsTrue(SurfaceProcessExit.IsFailure(exitCode));
        Assert.IsFalse(SurfaceProcessExit.IsKilled(exitCode));
    }

    [TestMethod]
    public void ExitCodeIsFormattedWithItsHexadecimalForm()
    {
        Assert.AreEqual("-1073741819 (0xC0000005)", SurfaceProcessExit.FormatExitCode(unchecked((int)0xC0000005)));
        Assert.AreEqual("1 (0x00000001)", SurfaceProcessExit.FormatExitCode(1));
    }

    [TestMethod]
    public void ExitCodeIsFormattedIndependentlyOfTheCurrentCulture()
    {
        // A culture can write the minus sign as U+2212 instead of '-': several do, and the regional settings of
        // Windows let the user choose it. The log keeps the form under which a failure code is documented.
        CultureInfo previousCulture = CultureInfo.CurrentCulture;
        CultureInfo minusSignCulture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        minusSignCulture.NumberFormat.NegativeSign = "\u2212";
        int exitCode = unchecked((int)0xC0000005);
        try
        {
            CultureInfo.CurrentCulture = minusSignCulture;

            // A plain interpolated string takes the minus sign of the current culture.
            Assert.AreEqual("\u22121073741819 (0xC0000005)", $"{exitCode} (0x{exitCode:X8})");
            Assert.AreEqual("-1073741819 (0xC0000005)", SurfaceProcessExit.FormatExitCode(exitCode));
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }
}
