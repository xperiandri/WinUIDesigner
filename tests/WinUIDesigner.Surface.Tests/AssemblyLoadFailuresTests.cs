// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinUIDesigner.Surface.Runtime;

[TestClass]
public sealed class AssemblyLoadFailuresTests
{
    // FUSION_E_REF_DEF_MISMATCH: "the located assembly's manifest definition does not match the assembly reference".
    private const int _RefDefMismatch = unchecked((int)0x80131040);

    private const string _WebView2Projection = "Microsoft.Web.WebView2.Core.Projection, Version=1.0.3967.48, Culture=neutral, PublicKeyToken=null";

    [TestMethod]
    [DataRow("Contoso.Resources, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null")]
    [DataRow("System.Resources.Extensions, Version=8.0.0.0, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51")]
    [DataRow("Microsoft.Windows.ApplicationModel.Resources.Projection, Version=2.0.0.0, Culture=neutral, PublicKeyToken=null")]
    [DataRow(@"C:\Staged\Contoso.Resources.dll")]
    // The runtime's probe for a satellite names its culture; an assembly asked for by a name without one is not a satellite.
    [DataRow("Contoso.resources")]
    [DataRow("Contoso.resources, Version=1.0.0.0, PublicKeyToken=null")]
    public void AMissingAssemblyWhoseNameMentionsResourcesIsLogged(string fileName)
    {
        Assert.IsTrue(AssemblyLoadFailures.IsWorthLogging(new FileNotFoundException(string.Empty, fileName)));
    }

    [TestMethod]
    [DataRow("Contoso.resources, Version=1.0.0.0, Culture=de-DE, PublicKeyToken=null")]
    [DataRow("WinUISurface.resources, Version=1.0.0.0, Culture=en, PublicKeyToken=null")]
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

    [TestMethod]
    public void TheCauseOfAFailureNamesTheAssemblyThatCouldNotBeLoaded()
    {
        // As a provider's constructor reports it: the failure to load inside the exception of the invocation.
        var exception = new TargetInvocationException(new FileLoadException(string.Empty, _WebView2Projection) { HResult = _RefDefMismatch });

        Assert.AreEqual(
            $"The assembly '{_WebView2Projection}' could not be loaded (FileLoadException 0x80131040).",
            AssemblyLoadFailures.DescribeCause(exception)
        );
    }

    [TestMethod]
    public void TheCauseOfAnotherFailureIsTheExceptionItself()
    {
        Assert.AreEqual(
            "It failed with InvalidOperationException 0x80131509.",
            AssemblyLoadFailures.DescribeCause(new InvalidOperationException("The type table is broken."))
        );
    }

    [TestMethod]
    public void FailuresWithTheSameCauseHaveTheSameKeyWhateverTheirMessage()
    {
        string key = AssemblyLoadFailures.GetCauseKey(new FileLoadException(string.Empty, _WebView2Projection) { HResult = _RefDefMismatch });

        Assert.AreEqual(key, AssemblyLoadFailures.GetCauseKey(new FileLoadException("Could not load file or assembly.", _WebView2Projection) { HResult = _RefDefMismatch }));
        Assert.AreEqual(key, AssemblyLoadFailures.GetCauseKey(new TargetInvocationException(new FileLoadException(string.Empty, _WebView2Projection) { HResult = _RefDefMismatch })));
    }

    [TestMethod]
    public void FailuresWithAnotherAssemblyHResultOrExceptionTypeHaveAnotherKey()
    {
        string key = AssemblyLoadFailures.GetCauseKey(new FileLoadException(string.Empty, _WebView2Projection) { HResult = _RefDefMismatch });

        // Only the assembly differs.
        Assert.AreNotEqual(key, AssemblyLoadFailures.GetCauseKey(new FileLoadException(string.Empty, "Microsoft.Windows.SDK.NET, Version=10.0.29000.0, Culture=neutral, PublicKeyToken=null") { HResult = _RefDefMismatch }));

        // Only the HRESULT differs: the one that a FileLoadException has by default.
        Assert.AreNotEqual(key, AssemblyLoadFailures.GetCauseKey(new FileLoadException(string.Empty, _WebView2Projection)));

        // Only the type of the exception differs.
        Assert.AreNotEqual(key, AssemblyLoadFailures.GetCauseKey(new FileNotFoundException(string.Empty, _WebView2Projection) { HResult = _RefDefMismatch }));
    }
}
