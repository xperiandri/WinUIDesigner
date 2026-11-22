// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinUIDesigner.Markup;

/// <summary>
/// Covers what the Visual Studio side reads from the text of a document before it sends the document to the
/// surface: whether platform prefixes are involved at all, whether an element is written under one (the XAML
/// cleaner replaces such an element), and which platform namespaces are missing from <c>mc:Ignorable</c>.
/// </summary>
[TestClass]
public sealed class UnoPlatformPrefixScanTests
{
    private const string _Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private const string _MarkupCompatibility = "http://schemas.openxmlformats.org/markup-compatibility/2006";

    // The markup of https://github.com/0x5bfa/WinUIDesigner/issues/5.
    private const string _IssuePage = """
        <Page xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
              xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
              xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
              xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
              xmlns:win="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
              xmlns:not_win="http://uno.ui/not_win"
              xmlns:skia="http://uno.ui/skia"
              mc:Ignorable="d not_win skia">
          <StackPanel>
            <win:Border xmlns:tv="using:WinUI.TableView">
              <tv:TableView />
            </win:Border>
            <not_win:Border>
              <ListView />
            </not_win:Border>
            <Grid win:IsDoubleTapEnabled="True">
              <Grid.ColumnDefinitions>
                <skia:ColumnDefinition Width="Auto" MinWidth="0" />
                <win:ColumnDefinition Width="Auto" MinWidth="128" />
              </Grid.ColumnDefinitions>
            </Grid>
            <TextBox win:SelectionFlyout="{x:Null}" win:TextReadingOrder="DetectFromContent" />
          </StackPanel>
        </Page>
        """;

    [TestMethod]
    public void IssuePageHasElementsTheCleanerReplaces()
    {
        XamlNamespaceScanResult scan = XamlNamespaceScanner.Scan(_IssuePage);

        Assert.IsTrue(scan.IsComplete);
        Assert.IsTrue(scan.HasPlatformNamespaces);
        Assert.IsTrue(scan.HasPlatformPrefixedObjectElements);
        Assert.IsTrue(XamlNamespacePolicies.MayAffect(scan, UnoPlatformPrefixes.DefaultTargetPlatform, evaluateCondition: null));
        Assert.AreEqual(0, UnoPlatformPrefixes.FindMissingIgnorable(scan).Count);
    }

    [TestMethod]
    public void PageWithPlatformAttributesOnlyKeepsItsPreparedXaml()
    {
        // The cleaner passes attributes and property elements of an ignorable namespace through as written.
        string xaml =
            $"<Page xmlns=\"{_Presentation}\" xmlns:mc=\"{_MarkupCompatibility}\" xmlns:win=\"{_Presentation}\" xmlns:android=\"http://uno.ui/android\" mc:Ignorable=\"android\">" +
            "<Grid win:Tag=\"w\" android:Tag=\"a\"><android:Grid.RowDefinitions><RowDefinition /></android:Grid.RowDefinitions><win:Border /></Grid></Page>";

        XamlNamespaceScanResult scan = XamlNamespaceScanner.Scan(xaml);

        Assert.IsTrue(scan.HasPlatformNamespaces);
        Assert.IsFalse(scan.HasPlatformPrefixedObjectElements);
        Assert.IsTrue(XamlNamespacePolicies.MayAffect(scan, UnoPlatformPrefixes.DefaultTargetPlatform, evaluateCondition: null));

        // Nothing is replaced, so the page keeps its prepared XAML and needs no second preparation.
        Assert.AreEqual(PlatformElementsBuild.PreparedXaml, UnoPlatformPrefixes.ChooseBuild(scan, reprepareRawElements: false, FailWhenCalled));
        Assert.AreEqual(PlatformElementsBuild.PreparedXaml, UnoPlatformPrefixes.ChooseBuild(scan, reprepareRawElements: true, FailWhenCalled));
    }

    [TestMethod]
    public void PageWithoutUnoNamespacesIsNotLookedAtFurther()
    {
        // win: alone is the presentation namespace: the cleaner resolves everything, nothing is replaced.
        string xaml = $"<Page xmlns=\"{_Presentation}\" xmlns:win=\"{_Presentation}\"><win:Border win:Tag=\"w\" /></Page>";

        XamlNamespaceScanResult scan = XamlNamespaceScanner.Scan(xaml);

        Assert.IsFalse(scan.HasPlatformNamespaces);
        Assert.IsFalse(scan.HasPlatformPrefixedObjectElements);
        Assert.AreEqual(0, UnoPlatformPrefixes.FindMissingIgnorable(scan).Count);
        Assert.AreEqual(PlatformElementsBuild.PreparedXaml, UnoPlatformPrefixes.ChooseBuild(scan, reprepareRawElements: true, FailWhenCalled));
    }

    [TestMethod]
    public void PageThatOnlyDeclaresAPlatformNamespaceKeepsItsPreparedXaml()
    {
        string xaml = $"<Page xmlns=\"{_Presentation}\" xmlns:android=\"http://uno.ui/android\" />";

        XamlNamespaceScanResult scan = XamlNamespaceScanner.Scan(xaml);

        Assert.IsTrue(scan.HasPlatformNamespaces);
        Assert.AreEqual(PlatformElementsBuild.PreparedXaml, UnoPlatformPrefixes.ChooseBuild(scan, reprepareRawElements: true, FailWhenCalled));
    }

    [TestMethod]
    public void PageWithAnObjectElementUnderAPlatformPrefixIsBuiltFromConstructionActions()
    {
        // The XAML cleaner replaced the element, and the second preparation is off: nothing is asked of it.
        XamlNamespaceScanResult scan = XamlNamespaceScanner.Scan(_IssuePage);

        Assert.AreEqual(PlatformElementsBuild.ConstructionActions, UnoPlatformPrefixes.ChooseBuild(scan, reprepareRawElements: false, FailWhenCalled));
    }

    [TestMethod]
    public void PageWithAnObjectElementUnderAPlatformPrefixIsPreparedAgainWhenThatWorks()
    {
        XamlNamespaceScanResult scan = XamlNamespaceScanner.Scan(_IssuePage);
        int calls = 0;

        PlatformElementsBuild build = UnoPlatformPrefixes.ChooseBuild(
            scan,
            reprepareRawElements: true,
            () =>
            {
                calls++;
                return true;
            }
        );

        Assert.AreEqual(PlatformElementsBuild.PreparedAgain, build);
        Assert.AreEqual(1, calls);
    }

    [TestMethod]
    public void PageThatCannotBePreparedAgainIsBuiltFromConstructionActions()
    {
        XamlNamespaceScanResult scan = XamlNamespaceScanner.Scan(_IssuePage);
        int calls = 0;

        PlatformElementsBuild build = UnoPlatformPrefixes.ChooseBuild(
            scan,
            reprepareRawElements: true,
            () =>
            {
                calls++;
                return false;
            }
        );

        Assert.AreEqual(PlatformElementsBuild.ConstructionActions, build);
        Assert.AreEqual(1, calls);
    }

    [TestMethod]
    public void PlatformNamespaceThatIsMissingFromIgnorableIsFoundWithItsPosition()
    {
        const string xaml = """
            <Page xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                  xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
                  xmlns:android="http://uno.ui/android"
                  xmlns:not_win="http://uno.ui/not_win#using:My.App"
                  mc:Ignorable="android">
              <not_win:Border />
            </Page>
            """;

        XamlNamespaceScanResult scan = XamlNamespaceScanner.Scan(xaml);
        IReadOnlyList<XamlNamespaceDeclaration> missing = UnoPlatformPrefixes.FindMissingIgnorable(scan);

        Assert.AreEqual(1, missing.Count);
        Assert.AreEqual("not_win", missing[0].Prefix);
        Assert.AreEqual("http://uno.ui/not_win#using:My.App", missing[0].Uri.Original);
        Assert.AreEqual(4, missing[0].LineNumber);
        Assert.IsTrue(missing[0].LinePosition > 0);

        // The prefix is applied all the same: its members are not part of the Windows preview.
        Assert.AreEqual(
            XamlPrefixDisposition.Drop,
            UnoPlatformPrefixes.Classify("not_win", missing[0].Uri, scan.IsIgnorable("not_win"), UnoPlatformPrefixes.DefaultTargetPlatform));
        Assert.IsTrue(scan.HasPlatformPrefixedObjectElements);
    }

    [TestMethod]
    [DataRow("<android:Border />")]
    [DataRow("<Grid android:Tag=\"1\" />")]
    [DataRow("<Grid><android:Grid.RowDefinitions /></Grid>")]
    public void PlatformNamespaceIsUsedByAnElementAnAttributeOrAPropertyElement(string content)
    {
        string xaml = $"<Page xmlns=\"{_Presentation}\" xmlns:android=\"http://uno.ui/android\">{content}</Page>";

        IReadOnlyList<XamlNamespaceDeclaration> missing = UnoPlatformPrefixes.FindMissingIgnorable(XamlNamespaceScanner.Scan(xaml));

        Assert.AreEqual(1, missing.Count);
        Assert.AreEqual("android", missing[0].Prefix);
    }

    [TestMethod]
    public void PlatformNamespaceThatNothingIsWrittenUnderIsNotMissingFromIgnorable()
    {
        // A resource dictionary that declares a platform prefix as boilerplate and never uses it, next to an
        // ignorable one that it does use: the XAML compiler has nothing to reject.
        const string xaml = """
            <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                xmlns:wasm="http://uno.ui/wasm"
                                xmlns:not_win="http://uno.ui/not_win"
                                xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
                                mc:Ignorable="not_win">
              <Style x:Key="Padded" TargetType="CalendarDatePicker">
                <Setter Property="Padding" Value="0,5" />
                <not_win:Setter Property="Margin" Value="4" />
              </Style>
            </ResourceDictionary>
            """;

        XamlNamespaceScanResult scan = XamlNamespaceScanner.Scan(xaml);

        Assert.IsTrue(scan.HasPlatformNamespaces);
        Assert.AreEqual(0, UnoPlatformPrefixes.FindMissingIgnorable(scan).Count);
    }

    [TestMethod]
    public void PlatformNamespaceIsUsedWhereItsPrefixIsBoundToIt()
    {
        // The page binds android to the platform namespace and writes nothing under it. A grid binds the prefix again,
        // to the platform namespace with a CLR namespace, and writes an element under it: that use belongs to the
        // declaration of the grid, so the one of the page is not reported.
        string xaml = $"""
            <Page xmlns="{_Presentation}" xmlns:android="http://uno.ui/android">
              <Grid xmlns:android="http://uno.ui/android#using:My.Droid">
                <android:Border />
              </Grid>
            </Page>
            """;

        IReadOnlyList<XamlNamespaceDeclaration> missing = UnoPlatformPrefixes.FindMissingIgnorable(XamlNamespaceScanner.Scan(xaml));

        Assert.AreEqual(1, missing.Count);
        Assert.AreEqual(2, missing[0].LineNumber);
        Assert.AreEqual("http://uno.ui/android#using:My.Droid", missing[0].Uri.Original);
    }

    [TestMethod]
    public void PlatformNamespaceWithAUsingSuffixIsUsedByItsPrefix()
    {
        string xaml = $"<Page xmlns=\"{_Presentation}\" xmlns:ios=\"http://uno.ui/ios#using:My.Ios\"><ios:Border /></Page>";

        IReadOnlyList<XamlNamespaceDeclaration> missing = UnoPlatformPrefixes.FindMissingIgnorable(XamlNamespaceScanner.Scan(xaml));

        Assert.AreEqual(1, missing.Count);
        Assert.AreEqual("http://uno.ui/ios#using:My.Ios", missing[0].Uri.Original);
    }

    [TestMethod]
    public void WarningNamesTheDocumentThePositionAndTheDeclaration()
    {
        const string xaml = """
            <Page xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                  xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
                  xmlns:not_win="http://uno.ui/not_win#using:My.App"
                  mc:Ignorable="d">
              <not_win:Border />
            </Page>
            """;
        XamlNamespaceDeclaration declaration = UnoPlatformPrefixes.FindMissingIgnorable(XamlNamespaceScanner.Scan(xaml)).Single();

        string message = UnoPlatformPrefixes.DescribeMissingIgnorable(@"C:\App\Page.xaml", declaration, reprepareRawElements: false);

        Assert.StartsWith($@"C:\App\Page.xaml(3,{declaration.LinePosition}): ", message);
        Assert.Contains("xmlns:not_win=\"http://uno.ui/not_win#using:My.App\" is an Uno platform namespace that the document uses but does not list in mc:Ignorable", message);
        Assert.Contains("so the Windows XAML compiler cannot resolve it", message);
    }

    [TestMethod]
    public void WarningSaysWhatBecomesOfTheMembersOfAPlatformPrefixInEachMode()
    {
        var declaration = new XamlNamespaceDeclaration("not_win", XamlNamespaceUri.Parse("http://uno.ui/not_win"), depth: 0, lineNumber: 1, linePosition: 1);

        // By default the object elements of the prefix reach the surface as construction actions.
        string byDefault = UnoPlatformPrefixes.DescribeMissingIgnorable(null, declaration, reprepareRawElements: false);
        Assert.Contains("drops its attributes and property elements regardless", byDefault);
        Assert.Contains("its object elements appear as elements of an unknown type", byDefault);
        Assert.Contains("unless WINUIDESIGNER_REPREPARE_RAW_PLATFORM_ELEMENTS=1 is set", byDefault);
        Assert.DoesNotContain("applies the platform prefix regardless", byDefault);

        // Prepared a second time, the surface drops everything under the prefix.
        string prepared = UnoPlatformPrefixes.DescribeMissingIgnorable(null, declaration, reprepareRawElements: true);
        Assert.Contains("The designer applies the platform prefix regardless.", prepared);
        Assert.DoesNotContain("unknown type", prepared);
    }

    [TestMethod]
    public void WarningLeavesTheMembersOfAPrefixTheTableDoesNotKnowAsWritten()
    {
        var declaration = new XamlNamespaceDeclaration("legacy", XamlNamespaceUri.Parse("http://uno.ui/legacy"), depth: 0, lineNumber: 1, linePosition: 1);

        foreach (bool reprepareRawElements in new[] { false, true })
        {
            string message = UnoPlatformPrefixes.DescribeMissingIgnorable(null, declaration, reprepareRawElements);

            Assert.Contains("Its members are left as written for the XAML reader to reject.", message);
            Assert.DoesNotContain("regardless", message);
        }
    }


    [TestMethod]
    [DataRow("win", "http://schemas.microsoft.com/winfx/2006/xaml/presentation", false, "win", "Keep")]
    [DataRow("win", "http://schemas.microsoft.com/winfx/2006/xaml/presentation", false, "android", "Drop")]
    [DataRow("android", "http://uno.ui/android", true, "win", "Drop")]
    [DataRow("android", "http://uno.ui/android", true, "android", "Keep")]
    [DataRow("not_win", "http://uno.ui/not_win", true, "win", "Drop")]
    [DataRow("not_win", "http://uno.ui/not_win", true, "skia", "Keep")]
    // Bound to the namespace the Windows XAML compiler resolves, the members stay whatever the prefix is called.
    [DataRow("not_win", "http://schemas.microsoft.com/winfx/2006/xaml/presentation", false, "win", "Keep")]
    [DataRow("android", "http://schemas.microsoft.com/winfx/2006/xaml/presentation", false, "win", "Keep")]
    [DataRow("skia", "using:SkiaSharp.Views.Windows", false, "win", "Leave")]
    [DataRow("legacy", "http://uno.ui/legacy", true, "win", "Drop")]
    [DataRow("", "http://schemas.microsoft.com/winfx/2006/xaml/presentation", false, "android", "Leave")]
    public void DocumentModelSideClassifiesLikeTheSurface(string prefix, string uri, bool isIgnorable, string target, string expected)
    {
        // The policy is shared source: the Visual Studio process compiles it for the .NET Framework.
        XamlPrefixDisposition actual = XamlNamespacePolicies.Classify(prefix, XamlNamespaceUri.Parse(uri), isIgnorable, target, evaluateCondition: null);

        Assert.AreEqual((XamlPrefixDisposition)Enum.Parse(typeof(XamlPrefixDisposition), expected), actual);
    }

    private static bool FailWhenCalled() => throw new AssertFailedException("The page needs no second preparation.");
}
