// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinUIDesigner.Markup;
using WinUIDesigner.Platform;

/// <summary>
/// Covers which member is reported as the one whose value the document model lost, when the member that it kept is dropped.
/// </summary>
[TestClass]
public sealed class XamlLostValueFinderTests
{
    [TestMethod]
    public void AttributeThatAppliesIsTheLostValue()
    {
        // c7 holds, c7n does not: the document model kept c7n:CornerRadius and dropped it.
        XamlAttributeName? lost = Find("c7n:CornerRadius", ["c7:CornerRadius", "c7n:CornerRadius"], droppedPrefixes: ["c7n"]);

        Assert.AreEqual("c7:CornerRadius", lost?.ToString());
    }

    [TestMethod]
    public void AttributeWithoutAPrefixAlwaysApplies()
    {
        XamlAttributeName? lost = Find("c99:Text", ["Text", "c99:Text"], droppedPrefixes: ["c99"]);

        Assert.AreEqual("Text", lost?.ToString());
    }

    [TestMethod]
    public void AttributeThatIsDroppedAsWellHasNoValueToLose()
    {
        Assert.IsNull(Find("c7n:CornerRadius", ["c99:CornerRadius", "c7n:CornerRadius"], droppedPrefixes: ["c99", "c7n"]));
    }

    [TestMethod]
    [DataRow("d:DataContext")]
    [DataRow("x:DataContext")]
    public void MemberOfAnotherNamespaceWithTheSameNameSetsAPropertyOfItsOwn(string other)
    {
        // d:DataContext is a design-time property that Visual Studio keeps apart from DataContext, whichever is written first.
        Assert.IsNull(Find("c99:DataContext", [other, "c99:DataContext"], droppedPrefixes: ["c99"]));
        Assert.IsNull(Find("c99:DataContext", ["mc:Ignorable", "c99:DataContext", other], droppedPrefixes: ["c99"]));
    }

    [TestMethod]
    public void MemberThatIsWrittenAfterTheDroppedOneIsNotAnotherValue()
    {
        Assert.IsNull(Find("c99:Text", ["c99:Text", "Text"], droppedPrefixes: ["c99"]));
    }

    [TestMethod]
    [DataRow("xmlns:Text")]
    [DataRow("nope:Text")]
    public void MemberWhosePrefixIsNotDeclaredNamesNoProperty(string undeclared)
    {
        Assert.IsNull(Find("c99:Text", [undeclared, "c99:Text"], droppedPrefixes: ["c99"]));
    }

    [TestMethod]
    public void MemberOfAnotherPropertyIsNotLost()
    {
        Assert.IsNull(Find("c7n:CornerRadius", ["c7:Padding", "Margin", "c7n:CornerRadius"], droppedPrefixes: ["c7n"]));
    }

    [TestMethod]
    [DataRow("Background")]
    [DataRow("c7:Background")]
    [DataRow("c7:Border.Background")]
    public void PropertyElementThatIsDroppedLosesTheValueOfAnEarlierMemberOfTheSameProperty(string earlier)
    {
        // The attributes of an element are read before its property elements, so any of them is earlier.
        XamlAttributeName? lost = Find("c7n:Border.Background", [earlier], droppedPrefixes: ["c7n"]);

        Assert.AreEqual(earlier, lost?.ToString());
    }

    [TestMethod]
    public void PropertyElementThatAppliesIsLostBehindADroppedOne()
    {
        XamlAttributeName? lost = Find("c7n:Border.Background", ["Opacity", "c7:Border.Background", "c7:Border.Child"], droppedPrefixes: ["c7n"]);

        Assert.AreEqual("c7:Border.Background", lost?.ToString());
    }

    [TestMethod]
    public void PropertyElementOfAnotherPropertyOrThatIsDroppedAsWellLosesNothing()
    {
        Assert.IsNull(Find("c7n:Border.Background", ["c7:Border.Child", "Foreground"], droppedPrefixes: ["c7n"]));
        Assert.IsNull(Find("c7n:Border.Background", ["c99:Border.Background", "Opacity"], droppedPrefixes: ["c7n", "c99"]));
    }

    [TestMethod]
    public void FirstMemberThatAppliesIsNamed()
    {
        XamlAttributeName? lost = Find("c7n:Text", ["c99:Text", "Text", "c7:Text", "c7n:Text"], droppedPrefixes: ["c99", "c7n"]);

        Assert.AreEqual("Text", lost?.ToString());
    }

    private static XamlAttributeName? Find(string droppedMember, string[] members, string[] droppedPrefixes)
    {
        return XamlLostValueFinder.Find(
            Parse(droppedMember),
            members.Select(Parse).ToArray(),
            GetBaseNamespace,
            prefix => droppedPrefixes.Contains(prefix) ? XamlPrefixDisposition.Drop : XamlPrefixDisposition.Keep
        );
    }

    // c7, c7n and c99 are conditional prefixes of the presentation namespace; xmlns is no namespace of the document.
    private static string? GetBaseNamespace(string prefix)
    {
        return prefix switch
        {
            "c7" or "c7n" or "c99" => XamlNamespaces.Presentation,
            "d" => XamlNamespaces.BlendDesign,
            "x" => XamlNamespaces.XamlLanguage,
            "mc" => XamlNamespaces.MarkupCompatibility,
            _ => null,
        };
    }

    private static XamlAttributeName Parse(string text)
    {
        int colon = text.IndexOf(':');
        return colon < 0
            ? new XamlAttributeName(string.Empty, text)
            : new XamlAttributeName(text.Substring(0, colon), text.Substring(colon + 1));
    }
}
