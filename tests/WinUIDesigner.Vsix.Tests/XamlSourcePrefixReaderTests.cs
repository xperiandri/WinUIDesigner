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
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinUIDesigner.Platform;

/// <summary>
/// Reads prefixes through the source references of Visual Studio's document model.
/// </summary>
/// <remarks>
/// The references are built by hand, the way Visual Studio's XAML parser builds them: the range of an element
/// covers the element, its start tag is tracked only when the element has content, and the range of an attribute
/// starts at its name. The tests check how the extension reads such references, not the ranges the parser produces.
/// </remarks>
[TestClass]
public sealed class XamlSourcePrefixReaderTests
{
    private const string _Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private const string _Android = "http://uno.ui/android";
    private const string _Condition = "IsApiContractPresent(Windows.Foundation.UniversalApiContract,7)";

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
        Assert.AreEqual(_Presentation, prefix.Namespace?.Value);
        Assert.AreEqual(_Presentation, prefix.Uri.Original);
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
        Assert.AreEqual(_Presentation, prefix.Namespace?.Value);
    }

    [TestMethod]
    public void SelfClosingElementIsReadFromItsRange()
    {
        var document = new SampleDocument();

        XamlSourcePrefix prefix = XamlSourcePrefixReader.ReadContainerPrefix(document.TextBlock);

        Assert.IsNull(document.TextBlock.StartTagRange);
        Assert.AreEqual("android", prefix.Prefix);
        Assert.AreEqual(_Android, prefix.Namespace?.Value);
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

        Assert.AreEqual(_Android, prefix.ResolveNamespace("android"));
        Assert.AreEqual(_Presentation, prefix.ResolveNamespace(string.Empty));
        Assert.IsNull(prefix.ResolveNamespace("ios"));
    }

    [TestMethod]
    public void ConditionTheDesignerSplitOffIsPartOfTheUri()
    {
        var document = new SampleDocument();

        var prefix = new XamlSourcePrefix("c7", new XmlNamespace(_Presentation, '?', _Condition), isIgnorable: false, document.Border);

        Assert.AreEqual($"{_Presentation}?{_Condition}", prefix.Uri.Original);
        Assert.AreEqual(_Presentation, prefix.Uri.BaseUri);
        Assert.AreEqual(_Condition, prefix.Uri.Condition);
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
            Declare(Grid, string.Empty, _Presentation);
            Declare(Grid, "win", _Presentation);
            Declare(Grid, "android", _Android);
            Declare(Grid, "mc", "http://schemas.openxmlformats.org/markup-compatibility/2006");
            Grid.AddIgnorableNamespace(new XmlNamespace(_Android));

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
}
#endif
