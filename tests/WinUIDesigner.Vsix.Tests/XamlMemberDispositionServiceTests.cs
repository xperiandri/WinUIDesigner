// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

// SimpleTextBuffer is only known to exist in Visual Studio 18; the Visual Studio 17.14 build leaves these tests out.
#if !VS2022
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using Microsoft.VisualStudio.DesignTools.Markup.DocumentModel;
using Microsoft.VisualStudio.DesignTools.Markup.Text;
using Microsoft.VisualStudio.DesignTools.Utility.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinUIDesigner.Markup;
using WinUIDesigner.Platform;

[TestClass]
public sealed class XamlMemberDispositionServiceTests
{
    private const string _Xaml = """<Page xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" />""";
    private const string _AndroidDeclaration = " xmlns:android=\"http://uno.ui/android\"";

    [TestMethod]
    public void NamespacesAreScannedOncePerText()
    {
        IDocumentRoot document = TextOnlyDocumentRoot.Create(new SimpleTextBuffer(_Xaml));
        var service = new XamlMemberDispositionService();

        XamlNamespaceScanResult namespaces = service.GetNamespaces(document);

        Assert.HasCount(1, namespaces.Declarations);
        Assert.AreSame(namespaces, service.GetNamespaces(document));
    }

    [TestMethod]
    public void NamespacesAreScannedAgainWhenTheTextChanges()
    {
        var buffer = new SimpleTextBuffer(_Xaml);
        IDocumentRoot document = TextOnlyDocumentRoot.Create(buffer);
        var service = new XamlMemberDispositionService();
        XamlNamespaceScanResult before = service.GetNamespaces(document);

        // This buffer reports the same version for every text, so the new length is what tells the texts apart.
        buffer.SetText(_Xaml.IndexOf(" />", StringComparison.Ordinal), 0, _AndroidDeclaration);
        XamlNamespaceScanResult after = service.GetNamespaces(document);

        Assert.HasCount(1, before.Declarations);
        CollectionAssert.AreEqual(new[] { string.Empty, "android" }, after.Declarations.Select(declaration => declaration.Prefix).ToArray());
    }

    [TestMethod]
    public void ChangingThePolicyInputsScansTheTextAgain()
    {
        IDocumentRoot document = TextOnlyDocumentRoot.Create(new SimpleTextBuffer(_Xaml));
        var service = new XamlMemberDispositionService();
        XamlNamespaceScanResult first = service.GetNamespaces(document);

        service.TargetPlatform = "android";
        XamlNamespaceScanResult second = service.GetNamespaces(document);
        service.EvaluateCondition = (_, _) => true;
        XamlNamespaceScanResult third = service.GetNamespaces(document);

        Assert.AreNotSame(first, second);
        Assert.AreNotSame(second, third);
    }

    [TestMethod]
    public void DocumentWithoutTextHasNoNamespaces()
    {
        var service = new XamlMemberDispositionService();

        Assert.AreSame(XamlNamespaceScanResult.Empty, service.GetNamespaces(null));
        Assert.AreSame(XamlNamespaceScanResult.Empty, service.GetNamespaces(TextOnlyDocumentRoot.Create(null)));
    }

    [TestMethod]
    public void MissingNodesAreLeftAlone()
    {
        var service = new XamlMemberDispositionService();

        Assert.AreEqual(XamlPrefixDisposition.Leave, service.GetNodeDisposition(null));
        Assert.AreEqual(XamlPrefixDisposition.Leave, service.GetPropertyDisposition(null, null));
    }

    /// <summary>
    /// A document root that has nothing but a text buffer, which is all the service reads of a document.
    /// </summary>
    private sealed class TextOnlyDocumentRoot(IMarkupTextBuffer? buffer) : RealProxy(typeof(IDocumentRoot))
    {
        public static IDocumentRoot Create(IMarkupTextBuffer? buffer) => (IDocumentRoot)new TextOnlyDocumentRoot(buffer).GetTransparentProxy();

        public override IMessage Invoke(IMessage message)
        {
            var call = (IMethodCallMessage)message;
            return call.MethodName == "get_TextBuffer"
                ? new ReturnMessage(buffer, null, 0, call.LogicalCallContext, call)
                : new ReturnMessage(new NotSupportedException($"The test document has no {call.MethodName}."), call);
        }
    }
}
#endif
