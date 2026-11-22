// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinUIDesigner.Surface.Runtime;

[TestClass]
public sealed class AssemblyLoadFailuresTests
{
    // FUSION_E_REF_DEF_MISMATCH: "the located assembly's manifest definition does not match the assembly reference".
    private const int _RefDefMismatch = unchecked((int)0x80131040);

    [TestMethod]
    [DataRow("Contoso.Resources, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null")]
    [DataRow("System.Resources.Extensions, Version=8.0.0.0, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51")]
    [DataRow("Microsoft.Windows.ApplicationModel.Resources.Projection, Version=2.0.0.0, Culture=neutral, PublicKeyToken=null")]
    [DataRow(@"C:\Staged\Contoso.Resources.dll")]
    public void AMissingAssemblyWhoseNameMentionsResourcesIsLogged(string fileName)
    {
        Assert.IsTrue(AssemblyLoadFailures.IsWorthLogging(new FileNotFoundException(string.Empty, fileName)));
    }

    [TestMethod]
    [DataRow("Contoso.resources, Version=1.0.0.0, Culture=de-DE, PublicKeyToken=null")]
    [DataRow("WinUISurface.resources, Version=1.0.0.0, Culture=en, PublicKeyToken=null")]
    [DataRow("Contoso.resources")]
    public void AMissingSatelliteAssemblyIsNotLogged(string fileName)
    {
        Assert.IsFalse(AssemblyLoadFailures.IsWorthLogging(new FileNotFoundException(string.Empty, fileName)));
    }

    [TestMethod]
    public void FailuresToLoadAnAssemblyOrATypeAreLogged()
    {
        Assert.IsTrue(AssemblyLoadFailures.IsWorthLogging(new FileNotFoundException()));
        Assert.IsTrue(AssemblyLoadFailures.IsWorthLogging(new FileLoadException()));
        Assert.IsTrue(AssemblyLoadFailures.IsWorthLogging(new BadImageFormatException()));
        Assert.IsTrue(AssemblyLoadFailures.IsWorthLogging(new TypeLoadException()));
        Assert.IsFalse(AssemblyLoadFailures.IsWorthLogging(new InvalidOperationException()));
    }

    [TestMethod]
    public void AFailureWithoutAMessageIsDescribedByItsFileNameAndHResult()
    {
        // As in the single-file surface, where the message of the exception is empty.
        var exception = new FileLoadException(string.Empty, "Microsoft.Web.WebView2.Core.Projection, Version=1.0.3967.48, Culture=neutral, PublicKeyToken=null")
        {
            HResult = _RefDefMismatch,
        };

        Assert.AreEqual(
            "FileLoadException 0x80131040 while loading 'Microsoft.Web.WebView2.Core.Projection, Version=1.0.3967.48, Culture=neutral, PublicKeyToken=null'",
            AssemblyLoadFailures.Describe(exception)
        );
    }

    [TestMethod]
    public void TheMessageFollowsTheDescription()
    {
        var exception = new FileNotFoundException("Could not load file or assembly.", "Contoso, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null");

        Assert.AreEqual(
            "FileNotFoundException 0x80070002 while loading 'Contoso, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null': Could not load file or assembly.",
            AssemblyLoadFailures.Describe(exception)
        );
    }

    [TestMethod]
    public void AFailureThatNamesNoFileIsDescribedByItsHResult()
    {
        Assert.AreEqual("TypeLoadException 0x80131522: Missing type.", AssemblyLoadFailures.Describe(new TypeLoadException("Missing type.")));
    }
}
