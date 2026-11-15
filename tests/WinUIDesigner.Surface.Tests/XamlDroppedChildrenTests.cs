// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System.Xml.Linq;
using WinUIDesigner.Markup;
using WinUIDesigner.Surface.Markup;

/// <summary>
/// Pins where the rewrite of prepared XAML reports the elements it removed from collections, and how the surface
/// marks their owners for the XAML reader. A position counts the items that precede the element in the collection as
/// Visual Studio's document model sees it: dropped elements included, property elements and elements the model has no
/// node for excluded.
/// </summary>
[TestClass]
public sealed class XamlDroppedChildrenTests
{
    private const string _Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private const string _XamlLanguage = "http://schemas.microsoft.com/winfx/2006/xaml";
    private const string _MarkupCompatibility = "http://schemas.openxmlformats.org/markup-compatibility/2006";
    private const string _Contract = "Windows.Foundation.UniversalApiContract";
    private const string _Namespaces =
        $"xmlns=\"{_Presentation}\" xmlns:x=\"{_XamlLanguage}\" xmlns:mc=\"{_MarkupCompatibility}\" " +
        $"xmlns:c7=\"{_Presentation}?IsApiContractPresent({_Contract},7)\" " +
        $"xmlns:c7n=\"{_Presentation}?IsApiContractNotPresent({_Contract},7)\" " +
        $"xmlns:c99=\"{_Presentation}?IsApiContractPresent({_Contract},99)\" " +
        $"xmlns:c99n=\"{_Presentation}?IsApiContractNotPresent({_Contract},99)\"";

    [TestMethod]
    public void EveryCollectionOfTheSamplePageReportsItsDroppedElements()
    {
        string xaml = $"""
            <Page {_Namespaces}>
              <Page.Resources>
                <ResourceDictionary>
                  <ResourceDictionary.MergedDictionaries>
                    <ResourceDictionary />
                    <c99:ResourceDictionary />
                    <ResourceDictionary />
                  </ResourceDictionary.MergedDictionaries>
                  <Style x:Key="Heading" TargetType="TextBlock">
                    <Setter Property="FontSize" Value="20" />
                    <c7:Setter Property="FontWeight" Value="SemiBold" />
                    <c99:Setter Property="Foreground" Value="Red" />
                  </Style>
                </ResourceDictionary>
              </Page.Resources>
              <StackPanel>
                <TextBlock Text="heading" />
                <c7:TextBlock Text="7" />
                <c7n:TextBlock Text="not 7" />
                <c99:TextBlock Text="99" />
                <c99n:TextBlock Text="not 99" />
                <Border />
                <Border />
                <c99n:InfoBar />
                <c99:InfoBar />
                <Button />
                <Grid>
                  <Grid.ColumnDefinitions>
                    <c7:ColumnDefinition />
                    <c99:ColumnDefinition />
                    <ColumnDefinition />
                  </Grid.ColumnDefinitions>
                </Grid>
                <ListView>
                  <ListViewItem />
                  <c99:ListViewItem />
                  <ListViewItem />
                </ListView>
              </StackPanel>
            </Page>
            """;

        XamlNamespaceRewriteResult result = Rewrite(xaml, out _);

        CollectionAssert.AreEqual(
            new[]
            {
                "ResourceDictionary/ResourceDictionary.MergedDictionaries#1",
                "Style#2",
                "StackPanel#2",
                "StackPanel#3",
                "StackPanel#8",
                "Grid/Grid.ColumnDefinitions#1",
                "ListView#1",
            },
            Describe(result));
        Assert.AreEqual(7, result.DroppedElements);
    }

    [TestMethod]
    public void TheElementOfAReportCarriesItsLineAndTheOwnerIsTheLoadedElement()
    {
        string xaml = $"""
            <StackPanel {_Namespaces}>
              <TextBlock />
              <c99:TextBlock Text="dropped" />
            </StackPanel>
            """;

        XamlNamespaceRewriteResult result = Rewrite(xaml, out XDocument document);

        XamlDroppedChild dropped = result.DroppedChildren.Single();
        Assert.AreSame(document.Root, dropped.Owner);
        Assert.IsNull(dropped.Property);
        Assert.AreEqual(1, dropped.Index);
        Assert.AreEqual("dropped", (string?)dropped.Element.Attribute("Text"));
        Assert.AreEqual(3, ((System.Xml.IXmlLineInfo)dropped.Element).LineNumber);
        Assert.IsNull(dropped.Element.Parent, "The element is no longer part of the document.");
    }

    [TestMethod]
    public void PropertyElementsAreNoItems()
    {
        string xaml = $"""
            <StackPanel {_Namespaces}>
              <StackPanel.Resources />
              <TextBlock />
              <StackPanel.Tag>x</StackPanel.Tag>
              <c99:TextBlock />
            </StackPanel>
            """;

        CollectionAssert.AreEqual(new[] { "StackPanel#1" }, Describe(Rewrite(xaml, out _)));
    }

    [TestMethod]
    public void ADroppedPropertyElementIsNoItem()
    {
        string xaml = $"""
            <StackPanel {_Namespaces}>
              <c99:StackPanel.Resources />
              <TextBlock />
              <c99:TextBlock />
              <Border><c99:Border.Child><TextBlock /></c99:Border.Child></Border>
            </StackPanel>
            """;

        XamlNamespaceRewriteResult result = Rewrite(xaml, out _);

        CollectionAssert.AreEqual(new[] { "StackPanel#1" }, Describe(result));
        Assert.AreEqual(3, result.DroppedElements);
    }

    [TestMethod]
    public void OnlyTheOutermostDroppedElementIsReported()
    {
        string xaml = $"""
            <StackPanel {_Namespaces}>
              <c99:StackPanel><c99:TextBlock /><TextBlock /></c99:StackPanel>
              <c99:TextBlock />
            </StackPanel>
            """;

        CollectionAssert.AreEqual(new[] { "StackPanel#0", "StackPanel#1" }, Describe(Rewrite(xaml, out _)));
    }

    [TestMethod]
    public void ADroppedRootIsKeptAndNotReported()
    {
        string xaml = $"""<c99:StackPanel {_Namespaces}><TextBlock /><c99:TextBlock /></c99:StackPanel>""";

        XamlNamespaceRewriteResult result = Rewrite(xaml, out XDocument document);

        Assert.IsNotNull(document.Root);
        CollectionAssert.AreEqual(new[] { "StackPanel#1" }, Describe(result));
    }

    [TestMethod]
    public void KeptElementsAreItems()
    {
        string xaml = $"""
            <StackPanel {_Namespaces}>
              <c7:TextBlock />
              <c99n:TextBlock />
              <c99:TextBlock />
              <c7n:TextBlock />
            </StackPanel>
            """;

        CollectionAssert.AreEqual(new[] { "StackPanel#2", "StackPanel#3" }, Describe(Rewrite(xaml, out _)));
    }

    [TestMethod]
    public void DirectiveObjectsAndObjectsOfOtherNamespacesAreItems()
    {
        string xaml = $"""
            <StackPanel {_Namespaces} xmlns:local="using:My.App">
              <x:String>text</x:String>
              <local:Control />
              <c99:TextBlock />
            </StackPanel>
            """;

        CollectionAssert.AreEqual(new[] { "StackPanel#2" }, Describe(Rewrite(xaml, out _)));
    }

    [TestMethod]
    public void ElementsOfAnIgnorablePlatformNamespaceAreNeitherReportedNorCounted()
    {
        // Visual Studio's document model has no node for an element under an ignorable Uno namespace.
        string xaml = $"""
            <StackPanel {_Namespaces} xmlns:not_win="http://uno.ui/not_win" xmlns:skia="http://uno.ui/skia" mc:Ignorable="not_win skia">
              <not_win:Border><TextBlock /></not_win:Border>
              <c99:TextBlock />
              <TextBlock />
              <Grid>
                <Grid.ColumnDefinitions>
                  <skia:ColumnDefinition />
                  <ColumnDefinition />
                  <c99:ColumnDefinition />
                </Grid.ColumnDefinitions>
              </Grid>
            </StackPanel>
            """;

        XamlNamespaceRewriteResult result = Rewrite(xaml, out _);

        CollectionAssert.AreEqual(new[] { "StackPanel#0", "Grid/Grid.ColumnDefinitions#1" }, Describe(result));
        Assert.AreEqual(4, result.DroppedElements);
    }

    [TestMethod]
    public void PlatformElementsOutsideMcIgnorableAreReported()
    {
        // Without mc:Ignorable the document model keeps the element as a node of an unknown type.
        string xaml = $"""
            <Grid {_Namespaces} xmlns:skia="http://uno.ui/skia">
              <Grid.ColumnDefinitions>
                <skia:ColumnDefinition />
                <ColumnDefinition />
              </Grid.ColumnDefinitions>
            </Grid>
            """;

        CollectionAssert.AreEqual(new[] { "Grid/Grid.ColumnDefinitions#0" }, Describe(Rewrite(xaml, out _)));
    }

    [TestMethod]
    public void IgnorableElementsThatNoPolicyDecidesAreNotCounted()
    {
        // The XAML reader skips them, and the document model has no node for a namespace it does not understand.
        string xaml = $"""
            <StackPanel {_Namespaces} xmlns:d="http://schemas.microsoft.com/expression/blend/2008" mc:Ignorable="d">
              <d:DesignOnly />
              <c99:TextBlock />
            </StackPanel>
            """;

        CollectionAssert.AreEqual(new[] { "StackPanel#0" }, Describe(Rewrite(xaml, out _)));
    }

    [TestMethod]
    public void AParentWithTextIsNotReportedButItsSiblingsAre()
    {
        string xaml = $"""
            <StackPanel {_Namespaces}>
              <TextBlock>Hello <c99:Run Text="x" /> world</TextBlock>
              <c99:TextBlock />
            </StackPanel>
            """;

        XamlNamespaceRewriteResult result = Rewrite(xaml, out _);

        CollectionAssert.AreEqual(new[] { "StackPanel#1" }, Describe(result));
        Assert.AreEqual(2, result.DroppedElements);
    }

    [TestMethod]
    public void APrefixDeclaredOnTheDroppedElementItselfIsUsed()
    {
        string xaml = $"""
            <StackPanel xmlns="{_Presentation}">
              <TextBlock />
              <c:TextBlock xmlns:c="{_Presentation}?IsApiContractPresent({_Contract},99)" />
              <TextBlock />
            </StackPanel>
            """;

        CollectionAssert.AreEqual(new[] { "StackPanel#1" }, Describe(Rewrite(xaml, out _)));
    }

    [TestMethod]
    public void MarksGroupThePositionsPerCollectionAndGiveEachOwnerOneMark()
    {
        string xaml = $"""
            <StackPanel {_Namespaces}>
              <Grid>
                <Grid.ColumnDefinitions>
                  <c99:ColumnDefinition />
                  <ColumnDefinition />
                  <c99:ColumnDefinition />
                </Grid.ColumnDefinitions>
                <c99:TextBlock />
                <TextBlock />
              </Grid>
              <c99:Grid />
            </StackPanel>
            """;

        XamlNamespaceRewriteResult result = Rewrite(xaml, out XDocument document);
        List<DroppedChildCollection> collections = DroppedChildOwnerMarks.Mark(document.Root!, result.DroppedChildren);

        // Owners are marked in the order of their first dropped element.
        XElement grid = document.Root!.Elements().Single(element => element.Name.LocalName == "Grid");
        CollectionAssert.AreEqual(
            new[] { "Grid/Grid.ColumnDefinitions=0,2 mark 0", "Grid=0 mark 0", "StackPanel=1 mark 1" },
            collections.Select(collection => $"{collection.Owner.Name.LocalName}{(collection.Property is null ? string.Empty : "/" + collection.Property.LocalName)}={string.Join(",", collection.Holes)} mark {collection.Mark}")
                .OrderBy(text => text, StringComparer.Ordinal)
                .ToArray());
        Assert.AreEqual("0", (string?)grid.Attribute(DroppedChildOwnerMarks.MarkName));
        Assert.AreEqual("1", (string?)document.Root!.Attribute(DroppedChildOwnerMarks.MarkName));

        // The reader sees the mark as the attached property DT.DroppedChildOwner of the surface types.
        string marked = document.ToString(SaveOptions.DisableFormatting);
        StringAssert.Contains(marked, "=\"using:XSurfUwp\"");
        StringAssert.Contains(marked, ":DT.DroppedChildOwner=\"1\"");
    }

    [TestMethod]
    public void MarksLeaveTemplateContentAndElementsOutsideTheLoadedElementAlone()
    {
        string xaml = $"""
            <Application {_Namespaces}>
              <Application.Resources>
                <ResourceDictionary>
                  <DataTemplate x:Key="Item">
                    <StackPanel><c99:TextBlock /><TextBlock /></StackPanel>
                  </DataTemplate>
                  <ResourceDictionary.MergedDictionaries>
                    <c99:ResourceDictionary />
                    <ResourceDictionary />
                  </ResourceDictionary.MergedDictionaries>
                </ResourceDictionary>
              </Application.Resources>
              <c99:Application.Tag />
            </Application>
            """;

        XamlNamespaceRewriteResult result = Rewrite(xaml, out XDocument document);
        XElement dictionary = document.Descendants().Single(element => element.Name.LocalName == "ResourceDictionary" && element.Parent?.Name.LocalName == "Application.Resources");
        List<DroppedChildCollection> collections = DroppedChildOwnerMarks.Mark(dictionary, result.DroppedChildren);

        CollectionAssert.AreEqual(new[] { "StackPanel#0", "ResourceDictionary/ResourceDictionary.MergedDictionaries#0" }, Describe(result), "The rewrite reports template content too.");
        Assert.AreEqual(1, collections.Count, "Only the merged dictionaries are created when the dictionary is loaded.");
        Assert.AreSame(dictionary, collections[0].Owner);
        Assert.IsFalse(document.Descendants().Any(element => element.Name.LocalName == "StackPanel" && element.Attribute(DroppedChildOwnerMarks.MarkName) is not null));
    }

    [TestMethod]
    public void UnmarkingRestoresTheDocumentAsItWasPrepared()
    {
        string xaml = $"""
            <StackPanel {_Namespaces}>
              <Grid><Grid.ColumnDefinitions><c99:ColumnDefinition /><ColumnDefinition /></Grid.ColumnDefinitions></Grid>
              <c99:TextBlock />
            </StackPanel>
            """;

        XamlNamespaceRewriteResult result = Rewrite(xaml, out XDocument document);
        string unmarked = document.ToString(SaveOptions.DisableFormatting);
        List<DroppedChildCollection> collections = DroppedChildOwnerMarks.Mark(document.Root!, result.DroppedChildren);
        Assert.AreNotEqual(unmarked, document.ToString(SaveOptions.DisableFormatting));

        DroppedChildOwnerMarks.Unmark(collections);

        Assert.IsFalse(document.Descendants().Any(element => element.Attribute(DroppedChildOwnerMarks.MarkName) is not null));
        Assert.AreEqual(unmarked, document.ToString(SaveOptions.DisableFormatting));
    }

    [TestMethod]
    public void ADocumentWithoutPolicyNamespacesReportsNothing()
    {
        string xaml = $"""<StackPanel xmlns="{_Presentation}"><TextBlock /><TextBlock /></StackPanel>""";

        XamlNamespaceRewriteResult result = Rewrite(xaml, out XDocument document);

        Assert.AreEqual(0, result.DroppedChildren.Count);
        Assert.AreEqual(0, DroppedChildOwnerMarks.Mark(document.Root!, result.DroppedChildren).Count);
    }

    /// <summary>
    /// Rewrites with the condition of every conditional prefix decided by its name, as the surface would decide it on a
    /// machine with contract 7 and without contract 99. Uno platform prefixes go through the real policy.
    /// </summary>
    private static XamlNamespaceRewriteResult Rewrite(string xaml, out XDocument document)
    {
        document = XDocument.Parse(xaml, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
        return XamlNamespaceRewriter.Rewrite(document, new XamlNamespaceRewriteOptions
        {
            SourceXaml = xaml,
            ClassifyOverride = (prefix, uri, _) => !uri.IsConditional
                ? null
                : uri.Original.Contains(",99)", StringComparison.Ordinal) == uri.Original.Contains("NotPresent", StringComparison.Ordinal)
                    ? XamlPrefixDisposition.Keep
                    : XamlPrefixDisposition.Drop,
        });
    }

    private static string[] Describe(XamlNamespaceRewriteResult result)
    {
        return result.DroppedChildren
            .Select(dropped => $"{dropped.Owner.Name.LocalName}{(dropped.Property is null ? string.Empty : "/" + dropped.Property.LocalName)}#{dropped.Index}")
            .ToArray();
    }
}
