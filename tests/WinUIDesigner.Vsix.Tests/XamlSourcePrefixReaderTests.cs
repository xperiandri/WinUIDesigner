// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

// The source references below are built from constructors that only Visual Studio 18 is known to have;
// the Visual Studio 17.14 build leaves these tests out.
#if !VS2022
using Microsoft.VisualStudio.DesignTools.Markup;
using Microsoft.VisualStudio.DesignTools.Markup.Metadata;
using Microsoft.VisualStudio.DesignTools.Markup.Text;
using Microsoft.VisualStudio.DesignTools.Markup.Xaml;
using Microsoft.VisualStudio.DesignTools.Markup.Xml;
using Microsoft.VisualStudio.DesignTools.Utility.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinUIDesigner.Markup;
using WinUIDesigner.Platform;

/// <summary>
/// Reads prefixes through the source references of Visual Studio's document model.
/// </summary>
/// <remarks>
/// Most references are built by hand, the way Visual Studio's XAML parser builds them: the range of an element
/// covers the element, its start tag is tracked only when the element has content, and the range of an attribute
/// starts at its name. Those tests check how the extension reads such references. Two tests hold the hand-built
/// ranges against the ranges that the designer's own XML parser reports for the same text, and read prefixes through
/// references made from its result, so a Visual Studio that reports other ranges fails them.
/// </remarks>
[TestClass]
public sealed class XamlSourcePrefixReaderTests
{
    private const string _Xaml = """
        <Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
              xmlns:win="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
              xmlns:android="http://uno.ui/android"
              xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
              mc:Ignorable="android">
          <win:Border win:Tag="w" Opacity="1">
            <win:Border.Child><android:TextBlock /></win:Border.Child>
          </win:Border>
          <ios:Button />
        </Grid>
        """;

    [TestMethod]
    public void PrefixedAttributeIsReadFromItsName()
    {
        var document = new SampleDocument();

        XamlSourcePrefix prefix = XamlSourcePrefixReader.ReadContainerPrefix(document.Attribute(document.Border, "win:Tag=\"w\""));

        Assert.IsTrue(prefix.IsKnown);
        Assert.AreEqual("win", prefix.Prefix);
        Assert.AreEqual(XamlNamespaces.Presentation, prefix.Namespace?.Value);
        Assert.AreEqual(XamlNamespaces.Presentation, prefix.Uri.Original);
        Assert.IsFalse(prefix.IsIgnorable);
    }

    [TestMethod]
    public void UnprefixedAttributeIsInNoNamespace()
    {
        var document = new SampleDocument();

        XamlSourcePrefix prefix = XamlSourcePrefixReader.ReadContainerPrefix(document.Attribute(document.Border, "Opacity=\"1\""));

        Assert.IsTrue(prefix.IsKnown);
        Assert.AreEqual(string.Empty, prefix.Prefix);
        Assert.IsNull(prefix.Namespace);
    }

    [TestMethod]
    public void AttributeWhoseRangeDoesNotStartAtItsNameIsNotRead()
    {
        var document = new SampleDocument();

        XamlSourcePrefix prefix = XamlSourcePrefixReader.ReadContainerPrefix(document.Attribute(document.Border, "\"w\""));

        Assert.IsFalse(prefix.IsKnown);
    }

    [TestMethod]
    public void PropertyElementIsReadFromItsStartTag()
    {
        var document = new SampleDocument();

        XamlSourcePrefix prefix = XamlSourcePrefixReader.ReadContainerPrefix(document.Child);

        Assert.IsNotNull(document.Child.StartTagRange);
        Assert.AreEqual("win", prefix.Prefix);
        Assert.AreEqual(XamlNamespaces.Presentation, prefix.Namespace?.Value);
    }

    [TestMethod]
    public void SelfClosingElementIsReadFromItsRange()
    {
        var document = new SampleDocument();

        XamlSourcePrefix prefix = XamlSourcePrefixReader.ReadContainerPrefix(document.TextBlock);

        Assert.IsNull(document.TextBlock.StartTagRange);
        Assert.AreEqual("android", prefix.Prefix);
        Assert.AreEqual(TestXamlNamespaces.Android, prefix.Namespace?.Value);
    }

    [TestMethod]
    public void NamespaceListedInIgnorableOfAnAncestorIsIgnorable()
    {
        var document = new SampleDocument();

        Assert.IsTrue(XamlSourcePrefixReader.ReadContainerPrefix(document.TextBlock).IsIgnorable);
        Assert.IsFalse(XamlSourcePrefixReader.ReadContainerPrefix(document.Child).IsIgnorable);
    }

    [TestMethod]
    public void UndeclaredPrefixIsReadWithoutANamespace()
    {
        var document = new SampleDocument();

        XamlSourcePrefix prefix = XamlSourcePrefixReader.ReadContainerPrefix(document.Button);

        Assert.IsTrue(prefix.IsKnown);
        Assert.AreEqual("ios", prefix.Prefix);
        Assert.IsNull(prefix.Namespace);
    }

    [TestMethod]
    public void PropertySetThroughContentHasNoPrefix()
    {
        // The designer gives a content property no container of its own.
        XamlSourcePrefix prefix = XamlSourcePrefixReader.ReadContainerPrefix(null);

        Assert.IsFalse(prefix.IsKnown);
        Assert.AreEqual(string.Empty, prefix.Prefix);
    }

    [TestMethod]
    public void RangeAtTheEndOfTheTextIsNotRead()
    {
        var document = new SampleDocument();
        XmlAttributeReference attribute = document.Attribute(document.Border, "Opacity=\"1\"");
        attribute.SetTextRange(document.Buffer, document.Buffer.Length, 8);

        Assert.IsFalse(XamlSourcePrefixReader.ReadContainerPrefix(attribute).IsKnown);
    }

    [TestMethod]
    public void OtherPrefixesResolveWhereTheMemberIsWritten()
    {
        var document = new SampleDocument();

        XamlSourcePrefix prefix = XamlSourcePrefixReader.ReadContainerPrefix(document.TextBlock);

        Assert.AreEqual(TestXamlNamespaces.Android, prefix.ResolveNamespace("android"));
        Assert.AreEqual(XamlNamespaces.Presentation, prefix.ResolveNamespace(string.Empty));
        Assert.IsNull(prefix.ResolveNamespace("ios"));
    }

    [TestMethod]
    public void ConditionTheDesignerSplitOffIsPartOfTheUri()
    {
        var document = new SampleDocument();

        var prefix = new XamlSourcePrefix("c7", new XmlNamespace(XamlNamespaces.Presentation, '?', TestXamlNamespaces.Condition), isIgnorable: false, document.Border);

        Assert.AreEqual(TestXamlNamespaces.Conditional, prefix.Uri.Original);
        Assert.AreEqual(XamlNamespaces.Presentation, prefix.Uri.BaseUri);
        Assert.AreEqual(TestXamlNamespaces.Condition, prefix.Uri.Condition);
    }

    [TestMethod]
    public void ReferencesBuiltByHandHaveTheRangesTheDesignersParserReports()
    {
        // The tests above stand on these ranges; this one holds them against the designer's own parser.
        var byHand = new SampleDocument();
        var parsed = new ParsedDocument(_Xaml);

        (string Name, XmlElementReference ByHand)[] elements =
        [
            ("Grid", byHand.Grid),
            ("win:Border", byHand.Border),
            ("win:Border.Child", byHand.Child),
            ("android:TextBlock", byHand.TextBlock),
            ("ios:Button", byHand.Button),
        ];
        foreach ((string name, XmlElementReference expected) in elements)
        {
            XmlElementReference actual = parsed.Element(name);
            Assert.AreEqual(Describe(expected.TextRange), Describe(actual.TextRange), $"The range of <{name}>.");
            Assert.AreEqual(Describe(expected.StartTagRange), Describe(actual.StartTagRange), $"The start tag range of <{name}>.");
        }

        Assert.AreEqual(
            Describe(byHand.Attribute(byHand.Border, "win:Tag=\"w\"").TextRange),
            Describe(parsed.Attribute("win:Border", "win:Tag").TextRange),
            "The range of the attribute win:Tag."
        );
        Assert.AreEqual(
            Describe(byHand.Attribute(byHand.Border, "Opacity=\"1\"").TextRange),
            Describe(parsed.Attribute("win:Border", "Opacity").TextRange),
            "The range of the attribute Opacity."
        );
    }

    [TestMethod]
    public void PrefixesAreReadThroughTheReferencesTheDesignersParserBuilds()
    {
        var parsed = new ParsedDocument(_Xaml);

        XamlSourcePrefix prefixedAttribute = XamlSourcePrefixReader.ReadContainerPrefix(parsed.Attribute("win:Border", "win:Tag"));
        XamlSourcePrefix unprefixedAttribute = XamlSourcePrefixReader.ReadContainerPrefix(parsed.Attribute("win:Border", "Opacity"));
        XamlSourcePrefix propertyElement = XamlSourcePrefixReader.ReadContainerPrefix(parsed.Element("win:Border.Child"));
        XamlSourcePrefix selfClosingElement = XamlSourcePrefixReader.ReadContainerPrefix(parsed.Element("android:TextBlock"));

        Assert.AreEqual("win", prefixedAttribute.Prefix);
        Assert.AreEqual(XamlNamespaces.Presentation, prefixedAttribute.Namespace?.Value);
        Assert.IsTrue(unprefixedAttribute.IsKnown);
        Assert.AreEqual(string.Empty, unprefixedAttribute.Prefix);
        Assert.IsNull(unprefixedAttribute.Namespace);
        Assert.AreEqual("win", propertyElement.Prefix);
        Assert.AreEqual("android", selfClosingElement.Prefix);
        Assert.AreEqual(TestXamlNamespaces.Android, selfClosingElement.Namespace?.Value);
        Assert.IsTrue(selfClosingElement.IsIgnorable);
    }

    private static string Describe(ITextRange? range)
    {
        return range is null ? "none" : $"[{range.Offset}, {range.Offset + range.Length}) {_Xaml.Substring(range.Offset, range.Length)}";
    }

    /// <summary>
    /// The source references of <see cref="_Xaml"/>, built the way the designer's XAML parser builds them.
    /// </summary>
    private sealed class SampleDocument
    {
        public SampleDocument()
        {
            Buffer = new SimpleTextBuffer(_Xaml);
            var document = new XmlDocumentReference(Buffer, new SourceContext(null, 0, _Xaml.Length), "utf-8", beginTracking: true);

            Grid = Element(document, "<Grid", "</Grid>", "mc:Ignorable=\"android\">");
            Declare(Grid, string.Empty, XamlNamespaces.Presentation);
            Declare(Grid, "win", XamlNamespaces.Presentation);
            Declare(Grid, "android", TestXamlNamespaces.Android);
            Declare(Grid, "mc", XamlNamespaces.MarkupCompatibility);
            Grid.AddIgnorableNamespace(new XmlNamespace(TestXamlNamespaces.Android));

            Border = Element(Grid, "<win:Border ", "</win:Border>", "Opacity=\"1\">");
            Child = Element(Border, "<win:Border.Child>", "</win:Border.Child>", "<win:Border.Child>");
            TextBlock = Element(Child, "<android:TextBlock />", "<android:TextBlock />", startTagEnd: null);
            Button = Element(Grid, "<ios:Button />", "<ios:Button />", startTagEnd: null);
        }

        public SimpleTextBuffer Buffer { get; }

        public XmlElementReference Grid { get; }

        public XmlElementReference Border { get; }

        public XmlElementReference Child { get; }

        public XmlElementReference TextBlock { get; }

        public XmlElementReference Button { get; }

        public XmlAttributeReference Attribute(XmlElementReference owner, string text) => new(owner, Find(text, text));

        private static XmlElementReference Element(XmlContainerReference parent, string start, string end, string? startTagEnd)
        {
            // A self-closing element has no start tag of its own.
            SourceContext startTag = startTagEnd is null ? default : Find(start, startTagEnd);
            return new XmlElementReference(parent, Find(start, end), startTag, beginTracking: true, XmlNamespaceGenerator.PlatformIndependentInstance);
        }

        private static void Declare(XmlElementReference element, string prefix, string namespaceUri)
        {
            // "xmlns" declares the default namespace; "xmlns:p" declares prefix "p".
            Identifier name = prefix.Length == 0
                ? Identifier.For("xmlns")
                : new ComplexIdentifier(prefix) { Prefix = XmlnsPrefix.ToPrefix("xmlns") };
            var declaration = new XmlAttribute(name);
            declaration.AddChild(new Literal(namespaceUri));
            element.AddAttributeToPreserve(new XmlElementReference.Attribute(XmlElementReference.AttributeType.Xmlns, declaration, null));
        }

        /// <summary>
        /// Returns the range from the first <paramref name="start"/> to the end of the next <paramref name="end"/>.
        /// </summary>
        private static SourceContext Find(string start, string end)
        {
            int startOffset = _Xaml.IndexOf(start, StringComparison.Ordinal);
            Assert.IsGreaterThanOrEqualTo(0, startOffset, start);
            int endOffset = _Xaml.IndexOf(end, startOffset, StringComparison.Ordinal);
            Assert.IsGreaterThanOrEqualTo(0, endOffset, end);
            return new SourceContext(null, startOffset, endOffset + end.Length);
        }
    }

    /// <summary>
    /// The source references of a text, made from what the designer's own XML parser reports for it the way the
    /// designer's XAML parser makes them: an element from its parsed element, an attribute from the range of its
    /// parsed attribute, and the namespaces from the declarations that the element carries.
    /// </summary>
    /// <remarks>
    /// The XML parser is an internal class of the designer and is reached by reflection. Its XAML parser would need
    /// the metadata of a whole project and platform to run, and adds no range of its own.
    /// </remarks>
    private sealed class ParsedDocument
    {
        private readonly Dictionary<string, XmlElementReference> _elements = [];
        private readonly Dictionary<(string Element, string Attribute), XmlAttributeReference> _attributes = [];

        public ParsedDocument(string xaml)
        {
            XmlDocument xmlDocument = Parse(xaml);
            var document = new XmlDocumentReference(new SimpleTextBuffer(xaml), xmlDocument, beginTracking: true);
            Add(document, xmlDocument.RootElement);
        }

        public XmlElementReference Element(string name) => _elements[name];

        public XmlAttributeReference Attribute(string elementName, string attributeName) => _attributes[(elementName, attributeName)];

        private static XmlDocument Parse(string xaml)
        {
            // These tests are a tripwire: a Visual Studio that changes the internal parser fails them with a message
            // that names the member that is gone, not with a bare MissingMethodException or NullReferenceException.
            Type? parserType = typeof(XmlDocument).Assembly.GetType("Microsoft.VisualStudio.DesignTools.Markup.Xml.Parser");
            Assert.IsNotNull(parserType, "The designer's XML parser was not found, so the ranges it reports cannot be checked.");

            var parseDocument = parserType.GetMethod("ParseDocument", Type.EmptyTypes);
            Assert.IsNotNull(parseDocument, "The designer's XML parser no longer has ParseDocument(); its ranges cannot be checked.");

            var errors = new List<ErrorNode>();
            object? parser = null;
            try
            {
                parser = Activator.CreateInstance(parserType, new Document("test.xaml", new DocumentText(xaml)), errors);
            }
            catch (MissingMethodException)
            {
                Assert.Fail("The designer's XML parser no longer has a constructor that takes a document and a list of errors; its ranges cannot be checked.");
            }

            var xmlDocument = (XmlDocument)parseDocument.Invoke(parser, null)!;
            Assert.IsEmpty(errors, "The designer's XML parser reported errors for the sample text.");
            return xmlDocument;
        }

        private void Add(XmlContainerReference parent, XmlElement element)
        {
            string name = element.Name.ToString();
            var reference = new XmlElementReference(parent, element, beginTracking: true, XmlNamespaceGenerator.PlatformIndependentInstance);
            _elements[name] = reference;

            // The declarations come first: the prefixes of the other attributes resolve against them.
            for (Node? node = element.FirstAttribute; node is XmlAttribute attribute; node = node.NextNode)
            {
                if (IsDeclaration(attribute))
                {
                    reference.AddAttributeToPreserve(new XmlElementReference.Attribute(
                        XmlElementReference.AttributeType.Xmlns,
                        attribute,
                        new XmlAttributeReference(reference, attribute.SourceContext)
                    ));
                }
            }

            for (Node? node = element.FirstAttribute; node is XmlAttribute attribute; node = node.NextNode)
            {
                if (IsDeclaration(attribute))
                {
                    continue;
                }

                // mc:Ignorable lists the prefixes whose namespaces the element ignores.
                if (attribute.LocalName == "Ignorable" && Equals(reference.GetXmlNamespace(attribute.Prefix), XmlNamespace.CompatibilityXmlNamespace))
                {
                    foreach (string prefix in XmlUtilities.GetPrefixes(XmlUtilities.GetAttributeValue(attribute)))
                    {
                        if (reference.GetXmlNamespace(XmlnsPrefix.ToPrefix(prefix)) is { } ignorable)
                        {
                            reference.AddIgnorableNamespace(ignorable);
                        }
                    }
                }

                _attributes[(name, attribute.Name.ToString())] = new XmlAttributeReference(reference, attribute.SourceContext);
            }

            for (Node? child = element.FirstChild; child is not null; child = child.NextNode)
            {
                if (child is XmlElement childElement)
                {
                    Add(reference, childElement);
                }
            }
        }

        // "xmlns" declares the default namespace; "xmlns:p" declares prefix "p".
        private static bool IsDeclaration(XmlAttribute attribute) => attribute.Prefix.Value == "xmlns" || attribute.Name.ToString() == "xmlns";
    }
}
#endif
