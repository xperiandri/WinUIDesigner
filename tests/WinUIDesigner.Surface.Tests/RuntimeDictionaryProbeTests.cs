// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinUIDesigner.Markup;
using WinUIDesigner.Surface.Markup;

/// <summary>
/// The two halves of the probe that asks the WinUI runtime whether it loads a dictionary the way a document gets it
/// merged. The reader itself runs in the surface; what it is given and how its answer is judged is tested here.
/// </summary>
[TestClass]
public sealed class RuntimeDictionaryProbeTests
{
    private static readonly XNamespace _P = XamlNamespaces.Presentation;

    [TestMethod]
    [DataRow("ms-appx:///Ecierge.Uno.Controls/Styles.xaml")]
    [DataRow("ms-appx:///CommunityToolkit.WinUI.Controls.Segmented/Segmented/Segmented.xaml")]
    [DataRow("ms-appx:///Pkg/A&B.xaml")]
    [DataRow("ms-appx:///Pkg/\"Quoted\" <x>.xaml")]
    public void ProbeMergesTheDictionaryTheWayADocumentDoes(string uri)
    {
        XElement probe = XElement.Parse(RuntimeDictionaryProbe.CreateXaml(uri));

        // A dictionary with one merged dictionary that has a Source, and nothing else: the shape of App.xaml.
        Assert.AreEqual(_P + "ResourceDictionary", probe.Name);
        XElement merged = probe.Elements().Single();
        Assert.AreEqual(_P + "ResourceDictionary.MergedDictionaries", merged.Name);
        XElement source = merged.Elements().Single();
        Assert.AreEqual(_P + "ResourceDictionary", source.Name);
        Assert.AreEqual(uri, (string?)source.Attribute("Source"));
        Assert.IsFalse(source.HasElements);
    }

    [TestMethod]
    public void DictionaryThatLoadsAndProvidesResourcesIsLoadable()
    {
        Assert.IsNull(RuntimeDictionaryProbe.Judge(mergedDictionaries: 1, resources: 1));
        Assert.IsNull(RuntimeDictionaryProbe.Judge(mergedDictionaries: 1, resources: 132));
    }

    [TestMethod]
    [DataRow(0, 5, "merged 0 dictionaries for the source instead of one")]
    [DataRow(2, 5, "merged 2 dictionaries for the source instead of one")]
    [DataRow(1, 0, "provides no resources")]
    public void OtherAnswersGiveTheReasonTheDictionaryIsLeftOut(int mergedDictionaries, int resources, string expected)
    {
        string? reason = RuntimeDictionaryProbe.Judge(mergedDictionaries, resources);

        Assert.IsNotNull(reason);
        Assert.Contains(expected, reason);
    }
}
