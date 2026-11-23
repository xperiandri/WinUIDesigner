// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinUIDesigner.Surface.Runtime;

[TestClass]
public sealed class ReportedFailuresTests
{
    // FUSION_E_REF_DEF_MISMATCH: "the located assembly's manifest definition does not match the assembly reference".
    private const int _RefDefMismatch = unchecked((int)0x80131040);

    private const string _WebView2Projection = "Microsoft.Web.WebView2.Core.Projection, Version=1.0.3967.48, Culture=neutral, PublicKeyToken=null";

    private const string _ConsoleUiProvider = "type|Ecierge.Console.UI.XamlTypeInfo.XamlMetaDataProvider";

    private static FileLoadException CannotLoad(string assembly, string message = "") => new(message, assembly) { HResult = _RefDefMismatch };

    [TestMethod]
    public void AFailureOfAProviderIsNewOnlyTheFirstTime()
    {
        var reported = new ReportedFailures();

        Assert.IsTrue(reported.TryAdd(_ConsoleUiProvider, CannotLoad(_WebView2Projection)));

        // XAML asks for the next type, and the provider fails in the same way, whatever the message says.
        Assert.IsFalse(reported.TryAdd(_ConsoleUiProvider, CannotLoad(_WebView2Projection)));
        Assert.IsFalse(reported.TryAdd(_ConsoleUiProvider, CannotLoad(_WebView2Projection, "Could not load file or assembly.")));
    }

    [TestMethod]
    public void AnotherProviderThatFailsInTheSameWayIsNew()
    {
        var reported = new ReportedFailures();
        reported.TryAdd(_ConsoleUiProvider, CannotLoad(_WebView2Projection));

        Assert.IsTrue(reported.TryAdd("type|Ecierge.Console.Management.XamlTypeInfo.XamlMetaDataProvider", CannotLoad(_WebView2Projection)));
    }

    [TestMethod]
    public void AnotherSituationOfTheSameProviderIsNew()
    {
        var reported = new ReportedFailures();
        reported.TryAdd(_ConsoleUiProvider, CannotLoad(_WebView2Projection));

        Assert.IsTrue(reported.TryAdd("xmlns|Ecierge.Console.UI.XamlTypeInfo.XamlMetaDataProvider", CannotLoad(_WebView2Projection)));
    }

    [TestMethod]
    public void AnotherCauseOfTheSameProviderIsNew()
    {
        var reported = new ReportedFailures();
        reported.TryAdd(_ConsoleUiProvider, CannotLoad(_WebView2Projection));

        Assert.IsTrue(reported.TryAdd(_ConsoleUiProvider, CannotLoad("Microsoft.Windows.SDK.NET, Version=10.0.29000.0, Culture=neutral, PublicKeyToken=null")));
    }
}
