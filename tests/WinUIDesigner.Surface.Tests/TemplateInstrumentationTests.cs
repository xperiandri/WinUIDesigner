// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinUIDesigner.Surface.Runtime;

[TestClass]
public sealed class TemplateInstrumentationTests
{
    // The text of a template as Visual Studio sends it (shortened), with the three attributes on the root.
    private const string _Template = """
        <ns0:DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
              xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
              xmlns:ns0='using:Microsoft.UI.Xaml'
        xmlns:ns1='using:Microsoft.UI.Xaml.Controls' >
              <ns1:StackPanel DependencyObject.Microsoft_VisualStudio_DesignTools_TemplateDocumentId='104396'
                              DependencyObject.Microsoft_VisualStudio_DesignTools_TemplateNodeId='5'
                              DependencyObject.Microsoft_VisualStudio_DesignTools_TemplateHistoryVersion='3'
                              Orientation='Vertical'>
                <ns1:TextBlock Text='Hello' />
              </ns1:StackPanel>
        </ns0:DataTemplate>
        """;

    [TestMethod]
    public void InstrumentationAttributesAreRemovedAndTheOthersStay()
    {
        string result = TemplateInstrumentation.Remove(_Template);

        Assert.DoesNotContain("Microsoft_VisualStudio_DesignTools_", result);
        Assert.Contains("Orientation='Vertical'", result);
        Assert.Contains("<ns1:TextBlock Text='Hello' />", result);
        Assert.Contains("xmlns:ns1='using:Microsoft.UI.Xaml.Controls'", result);
    }

    [TestMethod]
    public void ResultIsStillWellFormedXml()
    {
        string result = TemplateInstrumentation.Remove(_Template);

        var document = System.Xml.Linq.XDocument.Parse(result);

        Assert.AreEqual("DataTemplate", document.Root!.Name.LocalName);
        Assert.AreEqual("Vertical", (string?)document.Root.Elements().Single().Attribute("Orientation"));
    }

    [TestMethod]
    [DataRow("""<A DependencyObject.Microsoft_VisualStudio_DesignTools_TemplateNodeId="7" Width="1" />""", """<A Width="1" />""")]
    [DataRow("""<A ns1:DependencyObject.Microsoft_VisualStudio_DesignTools_TemplateNodeId='7' Width='1' />""", """<A Width='1' />""")]
    [DataRow("""<A Width="1" DependencyObject.Microsoft_VisualStudio_DesignTools_TemplateNodeId = "7"/>""", """<A Width="1"/>""")]
    public void QuotesAndPrefixesAreHandled(string xaml, string expected)
    {
        Assert.AreEqual(expected, TemplateInstrumentation.Remove(xaml));
    }

    [TestMethod]
    public void InstrumentationOfEveryElementIsRemoved()
    {
        string xaml = """<A DependencyObject.Microsoft_VisualStudio_DesignTools_TemplateNodeId='1'><B DependencyObject.Microsoft_VisualStudio_DesignTools_TemplateNodeId='2' /></A>""";

        Assert.AreEqual("<A><B /></A>", TemplateInstrumentation.Remove(xaml));
    }

    [TestMethod]
    public void TextWithoutInstrumentationIsReturnedAsItIs()
    {
        string xaml = "<A Width='1'><B Grid.Row='0' /></A>";

        Assert.AreSame(xaml, TemplateInstrumentation.Remove(xaml));
    }

    [TestMethod]
    public void AttributeOfAnotherTypeIsNotInstrumentation()
    {
        // Only the attached properties that Visual Studio declares on DependencyObject are meant.
        string xaml = "<A Other.Microsoft_VisualStudio_DesignTools_TemplateNodeId='1' />";

        Assert.AreEqual(xaml, TemplateInstrumentation.Remove(xaml));
    }
}
