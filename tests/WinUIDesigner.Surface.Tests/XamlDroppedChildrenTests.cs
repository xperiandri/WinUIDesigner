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
    private const string _Namespaces =
        $"xmlns=\"{XamlNamespaces.Presentation}\" xmlns:x=\"{XamlNamespaces.XamlLanguage}\" xmlns:mc=\"{XamlNamespaces.MarkupCompatibility}\" " +
        $"xmlns:c7=\"{TestXamlNamespaces.Conditional}\" " +
        $"xmlns:c7n=\"{TestXamlNamespaces.ConditionalNotPresent}\" " +
        $"xmlns:c99=\"{TestXamlNamespaces.ConditionalFuture}\" " +
        $"xmlns:c99n=\"{TestXamlNamespaces.ConditionalFutureNotPresent}\"";

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
            Describe(result)
        );
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
            <StackPanel {_Namespaces} xmlns:not_win="{TestXamlNamespaces.NotWin}" xmlns:skia="{TestXamlNamespaces.Skia}" mc:Ignorable="not_win skia">
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
            <Grid {_Namespaces} xmlns:skia="{TestXamlNamespaces.Skia}">
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
            <StackPanel {_Namespaces} xmlns:d="{XamlNamespaces.BlendDesign}" mc:Ignorable="d">
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
    public void WhiteSpaceBetweenElementsIsNoText()
    {
        // The inlines of a TextBlock written on separate lines: the white space between them does not leave the
        // collection out, the elements are counted as they are.
        string xaml = $"""
            <TextBlock {_Namespaces}>
              <Run Text="a" />
              <c99:Run Text="b" />
              <Run Text="c" />
            </TextBlock>
            """;

        CollectionAssert.AreEqual(new[] { "TextBlock#1" }, Describe(Rewrite(xaml, out _)));
    }

    [TestMethod]
    public void APrefixDeclaredOnTheDroppedElementItselfIsUsed()
    {
        string xaml = $"""
            <StackPanel xmlns="{XamlNamespaces.Presentation}">
              <TextBlock />
              <c:TextBlock xmlns:c="{TestXamlNamespaces.ConditionalFuture}" />
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
            collections.Select(collection => $"{DescribeCollection(collection)} mark {collection.Mark}")
                .OrderBy(text => text, StringComparer.Ordinal)
                .ToArray()
        );
        Assert.AreEqual("0", (string?)grid.Attribute(DroppedChildOwnerMarks.MarkName));
        Assert.AreEqual("1", (string?)document.Root!.Attribute(DroppedChildOwnerMarks.MarkName));

        // The reader sees the mark as the attached property DT.DroppedChildOwner of the surface types.
        string marked = document.ToString(SaveOptions.DisableFormatting);
        Assert.Contains("=\"using:XSurfUwp\"", marked);
        Assert.Contains(":DT.DroppedChildOwner=\"1\"", marked);
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
        Assert.HasCount(1, collections, "Only the merged dictionaries are created when the dictionary is loaded.");
        Assert.AreSame(dictionary, collections[0].Owner);
        Assert.DoesNotContain(element => element.Name.LocalName == "StackPanel" && element.Attribute(DroppedChildOwnerMarks.MarkName) is not null, document.Descendants());
    }

    [TestMethod]
    public void AnExplicitCollectionElementIsMarkedThroughThePropertyElementItIsWrittenIn()
    {
        // WinUI's collections are no DependencyObject: the reader cannot set the mark on <TransitionCollection>.
        string xaml = $"""
            <StackPanel {_Namespaces}>
              <Grid>
                <Grid.ChildrenTransitions>
                  <TransitionCollection>
                    <c99:EntranceThemeTransition />
                    <AddDeleteThemeTransition />
                    <c99:ReorderThemeTransition />
                  </TransitionCollection>
                </Grid.ChildrenTransitions>
              </Grid>
              <c99:TextBlock />
            </StackPanel>
            """;

        XamlNamespaceRewriteResult result = Rewrite(xaml, out XDocument document);
        List<DroppedChildCollection> collections = DroppedChildOwnerMarks.Mark(document.Root!, result.DroppedChildren, owner => owner.Name.LocalName != "TransitionCollection");

        CollectionAssert.AreEqual(new[] { "Grid/Grid.ChildrenTransitions=0,2", "StackPanel=1" }, DescribeCollections(collections));
        Assert.DoesNotContain(
            element => element.Name.LocalName == "TransitionCollection" && element.Attribute(DroppedChildOwnerMarks.MarkName) is not null,
            document.Descendants(),
            "The collection element itself is not marked."
        );
        Assert.IsNotNull(document.Descendants().Single(element => element.Name.LocalName == "Grid").Attribute(DroppedChildOwnerMarks.MarkName));
    }

    [TestMethod]
    public void TheCollectionThatIsTheValueOfASetterIsMarkedThroughTheSetter()
    {
        string xaml = $"""
            <Style {_Namespaces} TargetType="Grid">
              <Setter Property="ChildrenTransitions">
                <Setter.Value>
                  <TransitionCollection>
                    <c99:EntranceThemeTransition />
                  </TransitionCollection>
                </Setter.Value>
              </Setter>
            </Style>
            """;

        XamlNamespaceRewriteResult result = Rewrite(xaml, out XDocument document);
        List<DroppedChildCollection> collections = DroppedChildOwnerMarks.Mark(document.Root!, result.DroppedChildren, owner => owner.Name.LocalName != "TransitionCollection");

        CollectionAssert.AreEqual(new[] { "Setter/Setter.Value=0" }, DescribeCollections(collections));
    }

    [TestMethod]
    public void AnOwnerThatCannotCarryTheMarkLeavesOnlyItsOwnCollectionWithoutHoles()
    {
        // A plain class of the project holds a collection. Its parent is a value of Grid.Tag, not an element that has
        // the collection as a property, so there is nothing to mark for it; the Grid next to it is marked as usual.
        string xaml = $"""
            <Grid {_Namespaces} xmlns:local="using:My.App">
              <Grid.Tag>
                <local:Model>
                  <local:Model.Items>
                    <c99:Item />
                  </local:Model.Items>
                </local:Model>
              </Grid.Tag>
              <c99:TextBlock />
            </Grid>
            """;

        XamlNamespaceRewriteResult result = Rewrite(xaml, out XDocument document);
        List<DroppedChildCollection> collections = DroppedChildOwnerMarks.Mark(document.Root!, result.DroppedChildren, owner => owner.Name.LocalName != "Model");

        CollectionAssert.AreEqual(new[] { "Grid=0" }, DescribeCollections(collections));
        Assert.DoesNotContain(element => element.Name.LocalName == "Model" && element.Attribute(DroppedChildOwnerMarks.MarkName) is not null, document.Descendants());
    }

    [TestMethod]
    public void NothingIsMarkedWhenNoElementAroundACollectionCanCarryTheMark()
    {
        string xaml = $"""
            <Grid {_Namespaces}>
              <Grid.ChildrenTransitions>
                <TransitionCollection>
                  <c99:EntranceThemeTransition />
                </TransitionCollection>
              </Grid.ChildrenTransitions>
            </Grid>
            """;

        XamlNamespaceRewriteResult result = Rewrite(xaml, out XDocument document);
        List<DroppedChildCollection> collections = DroppedChildOwnerMarks.Mark(document.Root!, result.DroppedChildren, _ => false);

        Assert.IsEmpty(collections);
        Assert.IsFalse(HasMarks(document));
    }

    [TestMethod]
    public void AnOwnerThatTheCheckFailsForIsMarkedAsIfItCouldCarryTheMark()
    {
        // The look-up of a project type fails when its assembly cannot be loaded. That must not fail the document: the
        // element is marked as it is when nothing is known about it, and the load without marks is the safety net.
        string xaml = $"""
            <StackPanel {_Namespaces}>
              <TextBlock />
              <c99:TextBlock />
            </StackPanel>
            """;

        XamlNamespaceRewriteResult result = Rewrite(xaml, out XDocument document);
        List<DroppedChildCollection> collections = DroppedChildOwnerMarks.Mark(
            document.Root!,
            result.DroppedChildren,
            _ => throw new FileNotFoundException("The assembly of the type cannot be loaded.")
        );

        CollectionAssert.AreEqual(new[] { "StackPanel=1" }, DescribeCollections(collections));
        Assert.IsTrue(HasMarks(document));
    }

    [TestMethod]
    public void AHolderThatTheCheckFailsForCarriesTheMarkOfAnExplicitCollection()
    {
        // The collection element cannot carry the mark, and the check of the element that holds it fails: the holder is
        // marked, as an element without information is.
        string xaml = $"""
            <Grid {_Namespaces}>
              <Grid.ChildrenTransitions>
                <TransitionCollection>
                  <c99:EntranceThemeTransition />
                </TransitionCollection>
              </Grid.ChildrenTransitions>
            </Grid>
            """;

        XamlNamespaceRewriteResult result = Rewrite(xaml, out XDocument document);
        List<DroppedChildCollection> collections = DroppedChildOwnerMarks.Mark(
            document.Root!,
            result.DroppedChildren,
            owner => owner.Name.LocalName == "TransitionCollection" ? false : throw new InvalidOperationException("The type cannot be looked up.")
        );

        CollectionAssert.AreEqual(new[] { "Grid/Grid.ChildrenTransitions=0" }, DescribeCollections(collections));
        Assert.IsNotNull(document.Root!.Attribute(DroppedChildOwnerMarks.MarkName));
    }

    [TestMethod]
    public void EveryElementIsAskedAboutOnceHoweverManyDroppedElementsItHolds()
    {
        // The two dropped elements of the panel ask about the same owner, and the two of the explicit collection about
        // the collection element and about the element that holds it.
        string xaml = $"""
            <StackPanel {_Namespaces}>
              <c99:TextBlock />
              <c99:TextBlock />
              <Grid>
                <Grid.ChildrenTransitions>
                  <TransitionCollection>
                    <c99:EntranceThemeTransition />
                    <c99:EntranceThemeTransition />
                  </TransitionCollection>
                </Grid.ChildrenTransitions>
              </Grid>
            </StackPanel>
            """;
        var asked = new List<string>();

        XamlNamespaceRewriteResult result = Rewrite(xaml, out XDocument document);
        List<DroppedChildCollection> collections = DroppedChildOwnerMarks.Mark(
            document.Root!,
            result.DroppedChildren,
            owner =>
            {
                asked.Add(owner.Name.LocalName);
                return owner.Name.LocalName != "TransitionCollection";
            }
        );

        CollectionAssert.AreEqual(new[] { "StackPanel=0,1", "Grid/Grid.ChildrenTransitions=0,1" }, DescribeCollections(collections));
        CollectionAssert.AreEqual(new[] { "StackPanel", "TransitionCollection", "Grid" }, asked);
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

        Assert.IsFalse(HasMarks(document));
        Assert.AreEqual(unmarked, document.ToString(SaveOptions.DisableFormatting));
    }

    [TestMethod]
    public void ADocumentThatLoadsWithItsMarksIsLoadedOnce()
    {
        List<DroppedChildCollection> collections = MarkedStackPanel(out XDocument document);
        int loads = 0;

        string loaded = DroppedChildOwnerMarks.Load(collections, () => Load(document, ref loads), out Exception? marksFailure);

        Assert.AreEqual(1, loads);
        Assert.IsNull(marksFailure);
        Assert.Contains(":DT.DroppedChildOwner=\"0\"", loaded, "The marks stay: the surface finds the owners through them.");
    }

    [TestMethod]
    public void ADocumentThatLoadsOnlyWithoutItsMarksIsLoadedAgainAndTheFirstFailureIsReturned()
    {
        // The reader fails on a mark; the same text without marks loads.
        List<DroppedChildCollection> collections = MarkedStackPanel(out XDocument document);
        int loads = 0;

        string loaded = DroppedChildOwnerMarks.Load(
            collections,
            () => Load(document, ref loads, failOnMarks: true),
            out Exception? marksFailure
        );

        Assert.AreEqual(2, loads);
        Assert.AreEqual("The reader cannot set the mark.", marksFailure?.Message);
        Assert.DoesNotContain("DT.DroppedChildOwner", loaded);
        Assert.IsFalse(HasMarks(document), "The marks are gone from the document.");
    }

    [TestMethod]
    public void ADocumentThatFailsForAnotherReasonThrowsTheFailureOfTheSecondLoad()
    {
        // A resource that is missing, or a constructor that throws, fails the load with and without marks: the marks
        // were not the reason, so there is nothing to report, and the failure is the one of the load without them.
        List<DroppedChildCollection> collections = MarkedStackPanel(out XDocument document);
        int loads = 0;

        InvalidOperationException failure = Assert.ThrowsExactly<InvalidOperationException>(() =>
            DroppedChildOwnerMarks.Load<string>(collections, () => throw new InvalidOperationException($"load {++loads}"), out _));

        Assert.AreEqual(2, loads);
        Assert.AreEqual("load 2", failure.Message);
        Assert.IsFalse(HasMarks(document));
    }

    [TestMethod]
    public void ADocumentWithoutMarksIsLoadedOnceEvenWhenItFails()
    {
        int loads = 0;

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            DroppedChildOwnerMarks.Load<string>([], () => throw new InvalidOperationException($"load {++loads}"), out _));

        Assert.AreEqual(1, loads);
    }

    [TestMethod]
    public void ADocumentWithoutPolicyNamespacesReportsNothing()
    {
        string xaml = $"""<StackPanel xmlns="{XamlNamespaces.Presentation}"><TextBlock /><TextBlock /></StackPanel>""";

        XamlNamespaceRewriteResult result = Rewrite(xaml, out XDocument document);

        Assert.IsEmpty(result.DroppedChildren);
        Assert.IsEmpty(DroppedChildOwnerMarks.Mark(document.Root!, result.DroppedChildren));
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

    /// <summary>
    /// Marks the owners of a document with one dropped element, so that it has a mark.
    /// </summary>
    private static List<DroppedChildCollection> MarkedStackPanel(out XDocument document)
    {
        XamlNamespaceRewriteResult result = Rewrite($"""<StackPanel {_Namespaces}><TextBlock /><c99:TextBlock /></StackPanel>""", out document);
        return DroppedChildOwnerMarks.Mark(document.Root!, result.DroppedChildren);
    }

    /// <summary>
    /// Stands in for the XAML reader: it returns the text it is given, and fails on a mark when told to.
    /// </summary>
    private static string Load(XDocument document, ref int loads, bool failOnMarks = false)
    {
        loads++;
        string text = document.ToString(SaveOptions.DisableFormatting);
        return failOnMarks && HasMarks(document) ? throw new InvalidOperationException("The reader cannot set the mark.") : text;
    }

    private static bool HasMarks(XDocument document)
    {
        return document.Descendants().Any(element => element.Attribute(DroppedChildOwnerMarks.MarkName) is not null);
    }

    private static string DescribeCollection(DroppedChildCollection collection)
    {
        return $"{collection.Owner.Name.LocalName}{PropertyName(collection.Property)}={string.Join(",", collection.Holes)}";
    }

    private static string[] DescribeCollections(List<DroppedChildCollection> collections) => collections.Select(DescribeCollection).ToArray();

    private static string[] Describe(XamlNamespaceRewriteResult result)
    {
        return result.DroppedChildren
            .Select(dropped => FormattableString.Invariant($"{dropped.Owner.Name.LocalName}{PropertyName(dropped.Property)}#{dropped.Index}"))
            .ToArray();
    }

    // The property element of a collection, as "/Type.Member", or nothing for the content of its owner.
    private static string PropertyName(XName? property) => property is null ? string.Empty : $"/{property.LocalName}";
}
