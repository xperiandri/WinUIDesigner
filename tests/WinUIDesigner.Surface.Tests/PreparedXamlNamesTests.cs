// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System.Text;
using System.Xml.Linq;
using WinUIDesigner.Surface.Markup;

/// <summary>
/// Covers what source mapping and the App.xaml check take an element of prepared XAML to be.
/// </summary>
[TestClass]
public sealed class PreparedXamlNamesTests
{
    [TestMethod]
    [DataRow("using:System.Text", "StringBuilder", true)]
    [DataRow("using:System.Text", "Encoder", false)]
    [DataRow("using:System", "StringBuilder", false)]
    [DataRow("clr-namespace:System.Text", "StringBuilder", true)]
    [DataRow("clr-namespace:System.Text;assembly=mscorlib", "StringBuilder", true)]
    [DataRow("clr-namespace:System;assembly=mscorlib", "StringBuilder", false)]
    public void ElementUnderAClrNamespaceIsMarkupOfTheTypeItNames(string namespaceName, string localName, bool expected)
    {
        Assert.AreEqual(expected, PreparedXamlNames.IsMarkupOf(XNamespace.Get(namespaceName) + localName, typeof(StringBuilder)));
    }

    [TestMethod]
    [DataRow("using:System.Text?IsApiContractNotPresent(Windows.Foundation.UniversalApiContract,7)")]
    [DataRow("using:System.Text?IsApiContractPresent(Windows.Foundation.UniversalApiContract,7)")]
    public void ElementUnderAConditionalUsingNamespaceIsMarkupOfNoType(string namespaceName)
    {
        // The XAML reader decides the condition on its own. An element it skipped must not take the
        // source position of an object that was loaded from another element of the same type.
        Assert.IsFalse(PreparedXamlNames.IsMarkupOf(XNamespace.Get(namespaceName) + "StringBuilder", typeof(StringBuilder)));
    }

    [TestMethod]
    [DataRow("using:Microsoft.UI.Xaml.Controls", true)]
    [DataRow("http://schemas.microsoft.com/winfx/2006/xaml/presentation", true)]
    [DataRow("using:Microsoft.UI.Xaml.Controls?IsApiContractPresent(Windows.Foundation.UniversalApiContract,99)", false)]
    [DataRow("http://schemas.microsoft.com/winfx/2006/xaml/presentation?IsApiContractPresent(Windows.Foundation.UniversalApiContract,99)", false)]
    [DataRow("using:My.Controls", false)]
    [DataRow("", false)]
    public void OnlyAnUnconditionalNamespaceOfXamlControlsResourcesCounts(string namespaceName, bool expected)
    {
        // The XAML reader skips App.xaml's own copy when its condition fails; the designer must not add one then.
        Assert.AreEqual(expected, PreparedXamlNames.IsXamlControlsNamespace(namespaceName));
    }
}
