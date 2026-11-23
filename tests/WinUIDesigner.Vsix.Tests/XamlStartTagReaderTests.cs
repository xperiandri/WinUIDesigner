// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinUIDesigner.Markup;

[TestClass]
public sealed class XamlStartTagReaderTests
{
    [TestMethod]
    [DataRow("<Border c7:CornerRadius=\"4\" c7n:CornerRadius=\"0\" Tag=\"t\" />", "c7:CornerRadius|c7n:CornerRadius|Tag")]
    [DataRow("<c7:Border\r\n    x:Name = 'a'\r\n\tGrid.Row=\"1\"\r\n    c7:Grid.Column=\"2\">", "x:Name|Grid.Row|c7:Grid.Column")]
    [DataRow("<Border Tag=\"a > b\" Text='say \"hi\"' Content=\"it's\"/>", "Tag|Text|Content")]
    [DataRow("<Page xmlns=\"http://example\" xmlns:c7=\"http://example?IsTypePresent(A)\" c7:Tag=\"1\">", "xmlns|xmlns:c7|c7:Tag")]
    [DataRow("  <Border Tag=\"1\"><TextBlock Text=\"inner\" /></Border>", "Tag")]
    [DataRow("<Border>", "")]
    [DataRow("<Border/>", "")]
    [DataRow("<Border", "")]
    public void AttributeNamesAreReadInTheOrderTheyAreWritten(string startTag, string expected)
    {
        IReadOnlyList<XamlAttributeName> names = XamlStartTagReader.ReadAttributeNames(startTag);

        Assert.AreEqual(expected, string.Join("|", names.Select(name => name.ToString())));
    }

    [TestMethod]
    public void PrefixAndLocalNameAreSeparated()
    {
        IReadOnlyList<XamlAttributeName> names = XamlStartTagReader.ReadAttributeNames("<Border c7:Grid.Row=\"1\" Tag=\"t\" />");

        Assert.AreEqual("c7", names[0].Prefix);
        Assert.AreEqual("Grid.Row", names[0].LocalName);
        Assert.AreEqual(string.Empty, names[1].Prefix);
        Assert.AreEqual("Tag", names[1].LocalName);
    }

    [TestMethod]
    // The names read before the malformed part are still returned.
    [DataRow("<Border Tag=\"1\" Broken", "Tag")]
    [DataRow("<Border Tag=\"1\" Broken=", "Tag")]
    [DataRow("<Border Tag=\"1\" Broken=value", "Tag")]
    [DataRow("<Border Tag=\"1\" Text=\"unterminated", "Tag")]
    [DataRow("<Border Tag=\"1\" a:b:c=\"2\" Text=\"3\"", "Tag")]
    [DataRow("<Border Tag=\"1\" :b=\"2\"", "Tag")]
    [DataRow("<Border Tag=\"1\" a:=\"2\"", "Tag")]
    [DataRow("</Border>", "")]
    [DataRow("Border Tag=\"1\"", "")]
    [DataRow("", "")]
    [DataRow(null, "")]
    public void MalformedTextDoesNotThrow(string? startTag, string expected)
    {
        IReadOnlyList<XamlAttributeName> names = XamlStartTagReader.ReadAttributeNames(startTag);

        Assert.AreEqual(expected, string.Join("|", names.Select(name => name.ToString())));
    }

    [TestMethod]
    [DataRow("c7:CornerRadius=\"4\"", "c7", "CornerRadius")]
    [DataRow("  c7:Grid.Row = \"1\"", "c7", "Grid.Row")]
    [DataRow("Tag='t'", "", "Tag")]
    [DataRow("Tag", "", "Tag")]
    public void AttributeNameIsReadFromTheAttributeText(string attribute, string prefix, string localName)
    {
        Assert.IsTrue(XamlStartTagReader.TryReadAttributeName(attribute, out XamlAttributeName name));

        Assert.AreEqual(prefix, name.Prefix);
        Assert.AreEqual(localName, name.LocalName);
    }

    [TestMethod]
    [DataRow("<c7:Border.Background>", "c7", "Border.Background")]
    [DataRow("  <Border Tag=\"1\">", "", "Border")]
    [DataRow("<Border/>", "", "Border")]
    [DataRow("<c7:Border.Background><AcrylicBrush /></c7:Border.Background>", "c7", "Border.Background")]
    public void ElementNameIsReadFromTheStartTag(string startTag, string prefix, string localName)
    {
        Assert.IsTrue(XamlStartTagReader.TryReadElementName(startTag, out XamlAttributeName name));

        Assert.AreEqual(prefix, name.Prefix);
        Assert.AreEqual(localName, name.LocalName);
    }

    [TestMethod]
    [DataRow("Border")]
    [DataRow("</Border>")]
    [DataRow("<>")]
    [DataRow("<:Border>")]
    [DataRow("<a:b:c>")]
    [DataRow("<1a>")]
    [DataRow("   ")]
    [DataRow("")]
    [DataRow(null)]
    public void TextThatDoesNotBeginWithAStartTagHasNoElementName(string? text)
    {
        Assert.IsFalse(XamlStartTagReader.TryReadElementName(text, out XamlAttributeName name));

        Assert.AreEqual(string.Empty, name.LocalName);
    }

    [TestMethod]
    [DataRow("\r\n  <c7:Border.Background><SolidColorBrush /></c7:Border.Background>\r\n  ", "c7:Border.Background")]
    [DataRow("<c7:Border.Background><SolidColorBrush /></c7:Border.Background><c7n:Border.Background><AcrylicBrush /></c7n:Border.Background>", "c7:Border.Background|c7n:Border.Background")]
    [DataRow("<Border.Child><TextBlock /></Border.Child><Border.Background />", "Border.Child|Border.Background")]
    // A child element is no property element, and nothing inside a child is a child of the element.
    [DataRow("<TextBlock Text=\"a\" /><Button><Button.Content>x</Button.Content></Button>", "")]
    [DataRow("<c7:Border.Child><Grid><Grid.RowDefinitions><RowDefinition /></Grid.RowDefinitions></Grid></c7:Border.Child>", "c7:Border.Child")]
    // Comments, CDATA sections and processing instructions are skipped, with what they contain.
    [DataRow("<!-- <c7:Border.Background /> --><c7n:Border.Background />", "c7n:Border.Background")]
    [DataRow("<![CDATA[ <c7:Border.Background /> ]]><?x <c7:Border.Child /> ?><c7n:Border.Background />", "c7n:Border.Background")]
    // The value of an attribute may contain the characters of a tag.
    [DataRow("<TextBlock Text=\"a > <b\" Tag='/>' /><c7:Border.Background />", "c7:Border.Background")]
    public void PropertyElementsAreReadFromTheContentOfAnElement(string content, string expected)
    {
        IReadOnlyList<XamlAttributeName> names = XamlStartTagReader.ReadPropertyElementNames(content);

        Assert.AreEqual(expected, string.Join("|", names.Select(name => name.ToString())));
    }

    [TestMethod]
    // What is read before the malformed text is still returned.
    [DataRow("<c7:Border.Background /><c7n:Border.Background", "c7:Border.Background")]
    [DataRow("<c7:Border.Background /><!-- unterminated <c7n:Border.Background />", "c7:Border.Background")]
    [DataRow("<c7:Border.Background /><TextBlock Text=\"unterminated", "c7:Border.Background")]
    // The end tag of the element itself ends its content.
    [DataRow("<c7:Border.Background /></Border><c7n:Border.Background />", "c7:Border.Background")]
    [DataRow("</Border>", "")]
    [DataRow("just text", "")]
    [DataRow("", "")]
    [DataRow(null, "")]
    public void MalformedContentDoesNotThrow(string? content, string expected)
    {
        IReadOnlyList<XamlAttributeName> names = XamlStartTagReader.ReadPropertyElementNames(content);

        Assert.AreEqual(expected, string.Join("|", names.Select(name => name.ToString())));
    }

    [TestMethod]
    [DataRow("=\"4\"")]
    [DataRow(":CornerRadius=\"4\"")]
    [DataRow("c7:=\"4\"")]
    [DataRow("a:b:c=\"4\"")]
    [DataRow("\"w\"")]
    [DataRow("1abc=\"4\"")]
    [DataRow("a:1b=\"4\"")]
    [DataRow("   ")]
    [DataRow("")]
    [DataRow(null)]
    public void TextThatDoesNotBeginWithANameIsNoAttribute(string? attribute)
    {
        Assert.IsFalse(XamlStartTagReader.TryReadAttributeName(attribute, out XamlAttributeName name));

        Assert.AreEqual(string.Empty, name.Prefix);
        Assert.AreEqual(string.Empty, name.LocalName);
    }
}
