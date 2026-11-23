// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinUIDesigner.Markup;
using WinUIDesigner.Surface.Markup;

/// <summary>
/// Finding the resources that Visual Studio left out of a prepared copy, by comparing it with the copy of the same
/// file that the build laid out. The styles are those of the Ecierge solution that Visual Studio removed at a cold
/// start (a style of a type of the project, and one of a Syncfusion type), from the copy of <c>ErrorStyles.xaml</c>.
/// </summary>
[TestClass]
public sealed class PreparedCopyComparisonTests
{
    [TestMethod]
    public void ResourcesOfTheBuiltCopyThatThePreparedCopyLacksAreFound()
    {
        XElement prepared = Parse("""
            <Style x:Key="PlainErrorStyle" TargetType="TextBox" />
            <SolidColorBrush x:Key="Accent" Color="Red" />
            """);
        XElement built = Parse("""
            <Style x:Key="PlainErrorStyle" TargetType="TextBox" />
            <Style x:Key="LoadingTextBoxErrorStyle" TargetType="local:LoadingTextBox" />
            <Style x:Key="SfComboBoxErrorStyle" TargetType="sfe:SfComboBox" />
            <SolidColorBrush x:Key="Accent" Color="Red" />
            """);

        IReadOnlyList<LeftOutResource> leftOut = PreparedCopyComparison.FindLeftOut(prepared, built, out int resourcesInBuilt);

        Assert.AreEqual(4, resourcesInBuilt);
        CollectionAssert.AreEqual(
            new[] { "LoadingTextBoxErrorStyle (Style of local:LoadingTextBox)", "SfComboBoxErrorStyle (Style of sfe:SfComboBox)" },
            leftOut.Select(resource => resource.ToString()).ToArray()
        );
    }

    [TestMethod]
    public void ResourcesOnlyThePreparedCopyHasAreNoProblem()
    {
        // The cleaner adds fallback resources, and the file may have been edited after the build.
        XElement prepared = Parse("""
            <Style x:Key="PlainErrorStyle" TargetType="TextBox" />
            <Style x:Key="AddedLater" TargetType="Button" />
            """);
        XElement built = Parse("""<Style x:Key="PlainErrorStyle" TargetType="TextBox" />""");

        Assert.IsEmpty(PreparedCopyComparison.FindLeftOut(prepared, built, out _));
    }

    [TestMethod]
    public void KeyThatEachThemeDictionaryRepeatsIsCountedPerDictionary()
    {
        // The dark dictionary lost its brush; the light one has it, and the key is the same.
        XElement prepared = Parse("""
            <ResourceDictionary.ThemeDictionaries>
              <ResourceDictionary x:Key="Light"><SolidColorBrush x:Key="Accent" Color="White" /></ResourceDictionary>
              <ResourceDictionary x:Key="Dark" />
            </ResourceDictionary.ThemeDictionaries>
            """);
        XElement built = Parse("""
            <ResourceDictionary.ThemeDictionaries>
              <ResourceDictionary x:Key="Light"><SolidColorBrush x:Key="Accent" Color="White" /></ResourceDictionary>
              <ResourceDictionary x:Key="Dark"><SolidColorBrush x:Key="Accent" Color="Black" /></ResourceDictionary>
            </ResourceDictionary.ThemeDictionaries>
            """);

        LeftOutResource missing = PreparedCopyComparison.FindLeftOut(prepared, built, out int resourcesInBuilt).Single();

        Assert.AreEqual("Accent", missing.Key);
        Assert.AreEqual(2, resourcesInBuilt, "The brush of each of the two theme dictionaries; the dictionaries are no resources.");
    }

    [TestMethod]
    public void ImplicitStyleIsIdentifiedByItsTargetType()
    {
        XElement prepared = Parse("""<Style TargetType="Button" />""");
        XElement built = Parse("""
            <Style TargetType="Button" />
            <Style TargetType="local:LoadingTextBox" />
            """);

        LeftOutResource missing = PreparedCopyComparison.FindLeftOut(prepared, built, out _).Single();

        Assert.IsNull(missing.Key);
        Assert.AreEqual("TargetType=local:LoadingTextBox (Style)", missing.ToString());
    }

    [TestMethod]
    public void WhatTheReaderSkipsAndWhatHasNoKeyAreNoResources()
    {
        // mc:Ignorable names d, so the elements of d are no resources; property elements and setters have no key.
        XElement prepared = Parse("""<Style x:Key="Kept" TargetType="Button" />""");
        XElement built = XElement.Parse($"""
            <ResourceDictionary xmlns="{XamlNamespaces.Presentation}" xmlns:x="{XamlNamespaces.XamlLanguage}"
                xmlns:d="{XamlNamespaces.BlendDesign}" xmlns:mc="{XamlNamespaces.MarkupCompatibility}" mc:Ignorable="d">
              <Style x:Key="Kept" TargetType="Button"><Setter Property="Padding" Value="4" /></Style>
              <d:DesignData x:Key="DesignOnly" />
              <ResourceDictionary.MergedDictionaries><ResourceDictionary x:Key="Nested" /></ResourceDictionary.MergedDictionaries>
              <x:String>no key</x:String>
            </ResourceDictionary>
            """);

        Assert.IsEmpty(PreparedCopyComparison.FindLeftOut(prepared, built, out int resourcesInBuilt));
        Assert.AreEqual(1, resourcesInBuilt);
    }

    [TestMethod]
    public void MessageNamesTheDictionaryTheResourcesAndWhatToDo()
    {
        LeftOutResource[] leftOut =
        [
            new("Style", "LoadingTextBoxErrorStyle", "local:LoadingTextBox"),
            new("Style", "SfComboBoxErrorStyle", "sfe:SfComboBox"),
        ];

        string message = PreparedCopyComparison.Describe("Ecierge.Console.UI/Styles/ErrorStyles.xaml", leftOut, 14);

        Assert.StartsWith("Visual Studio's prepared copy of 'Ecierge.Console.UI/Styles/ErrorStyles.xaml' lacks 2 of the 14 resources that the built copy of the file has: ", message);
        Assert.Contains("LoadingTextBoxErrorStyle (Style of local:LoadingTextBox), SfComboBoxErrorStyle (Style of sfe:SfComboBox).", message);
        Assert.Contains("when it prepares a copy", message);
        Assert.Contains("reload the designer", message);
        Assert.Contains("If the resources were deleted from the file after the last build, ignore this.", message);
    }

    [TestMethod]
    public void MessageNamesOnlyTheFirstOnesAndCountsTheRest()
    {
        LeftOutResource[] leftOut = Enumerable.Range(0, PreparedCopyComparison.MaxNamed + 3).Select(index => new LeftOutResource("Style", $"Style{index}", null)).ToArray();

        string message = PreparedCopyComparison.Describe("Styles.xaml", leftOut, 20);

        Assert.Contains("lacks 11 of the 20 resources", message);
        Assert.Contains("Style7 (Style)", message);
        Assert.DoesNotContain("Style8", message);
        Assert.Contains(", and 3 more.", message);
    }

    private static XElement Parse(string body)
    {
        return XElement.Parse($"""<ResourceDictionary xmlns="{XamlNamespaces.Presentation}" xmlns:x="{XamlNamespaces.XamlLanguage}" xmlns:local="using:Ecierge.Uno.Controls" xmlns:sfe="using:Syncfusion.WinUI.Editors">{body}</ResourceDictionary>""");
    }
}
