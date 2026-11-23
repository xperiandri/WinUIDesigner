// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

// SimpleTextBuffer is only known to exist in Visual Studio 18; the Visual Studio 17.14 build leaves these tests out.
#if !VS2022
using System.Collections.Concurrent;
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
    private const string _Xaml = $"""<Page xmlns="{XamlNamespaces.Presentation}" />""";
    private const string _AndroidDeclaration = $" xmlns:android=\"{TestXamlNamespaces.Android}\"";

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
    public void NewAnswersDoNotReplaceTheVerdictsOfADocumentThatMayBeBeingBuilt()
    {
        IDocumentRoot document = TextOnlyDocumentRoot.Create(new SimpleTextBuffer(_Xaml));
        var service = new XamlMemberDispositionService();
        XamlNamespaceScanResult before = service.GetNamespaces(document);

        // A document is judged with one set of answers, also if the surface answers while it is built.
        service.AnswersChanged();
        XamlNamespaceScanResult after = service.GetNamespaces(document);

        Assert.AreSame(before, after);
    }

    [TestMethod]
    public void RefreshJudgesADocumentWithTheAnswersThatChangedSinceItWasLookedAt()
    {
        IDocumentRoot document = TextOnlyDocumentRoot.Create(new SimpleTextBuffer(_Xaml));
        var service = new XamlMemberDispositionService();
        XamlNamespaceScanResult first = service.GetNamespaces(document);

        // No answer changed, so there is nothing to look at again.
        service.Refresh(document);
        XamlNamespaceScanResult unchanged = service.GetNamespaces(document);

        service.AnswersChanged();
        service.Refresh(document);
        XamlNamespaceScanResult refreshed = service.GetNamespaces(document);

        // A state made with the answers of now stays.
        service.Refresh(document);
        XamlNamespaceScanResult kept = service.GetNamespaces(document);

        Assert.AreSame(first, unchanged);
        Assert.AreNotSame(first, refreshed);
        Assert.AreSame(refreshed, kept);
    }

    [TestMethod]
    public void RefreshHasNothingToDiscardForADocumentThatWasNeverLookedAt()
    {
        var service = new XamlMemberDispositionService();
        service.AnswersChanged();
        IDocumentRoot document = TextOnlyDocumentRoot.Create(new SimpleTextBuffer(_Xaml));

        service.Refresh(null);
        service.Refresh(document);
        service.Refresh(TextOnlyDocumentRoot.Create(null));

        Assert.HasCount(1, service.GetNamespaces(document).Declarations);
    }

    [TestMethod]
    public void ConditionHasOneAnswerWhileItsVerdictsAreKept()
    {
        var outcomes = new ConcurrentDictionary<string, bool?>(StringComparer.Ordinal);
        bool? answer = null;
        int calls = 0;
        XamlConditionEvaluator? pinned = XamlMemberDispositionService.Pin(
            (_, _) =>
            {
                calls++;
                return answer;
            },
            outcomes
        );
        Func<string, string?> declared = prefix => prefix == "cond" ? "using:My.App" : null;
        const string condition = "cond:FeatureFlagCondition(NewExperience)";

        // The element is judged before the surface answers, its attributes after: the same answer for all.
        bool? first = pinned!(condition, declared);
        answer = true;
        bool? second = pinned(condition, declared);

        Assert.IsNull(first);
        Assert.IsNull(second);
        Assert.AreEqual(1, calls);

        // Another condition, and the same text where its prefix is bound to another namespace, are other questions.
        Assert.AreEqual(true, pinned("cond:OtherCondition(NewExperience)", declared));
        Assert.AreEqual(true, pinned(condition, prefix => prefix == "cond" ? "using:Other.App" : null));
        Assert.AreEqual(3, calls);
    }

    [TestMethod]
    [DataRow("IsTypePresent(My.App.Card)", "IsTypeNotPresent(My.App.Card)")]
    [DataRow("IsTypeNotPresent(My.App.Card)", "IsTypePresent(My.App.Card)")]
    [DataRow("IsApiContractPresent(Windows.Foundation.UniversalApiContract,99)", "IsApiContractNotPresent(Windows.Foundation.UniversalApiContract,99)")]
    public void HalvesOfAnIfElsePairHaveOneAnswerWhileTheirVerdictsAreKept(string judgedFirst, string judgedLater)
    {
        var outcomes = new ConcurrentDictionary<string, bool?>(StringComparer.Ordinal);
        bool? answer = null;
        int calls = 0;
        XamlConditionEvaluator? pinned = XamlMemberDispositionService.Pin(
            (condition, _) =>
            {
                calls++;

                // The negated half is answered the other way round, as the evaluator of the answers does.
                return condition.Contains("NotPresent") ? !answer : answer;
            },
            outcomes
        );

        // One prefix of the pair is judged before the surface answers, the other one after it: they ask one question.
        bool? first = pinned!(judgedFirst, _ => null);
        answer = false;
        bool? later = pinned(judgedLater, _ => null);

        Assert.IsNull(first);
        Assert.IsNull(later);
        Assert.AreEqual(1, calls);
    }

    [TestMethod]
    [DataRow("IsTypePresent(My.App.Card)", "IsTypeNotPresent(My.App.Card)", false)]
    [DataRow("IsTypeNotPresent(My.App.Card)", "IsTypePresent(My.App.Card)", false)]
    [DataRow("IsTypePresent(My.App.Card)", "IsTypeNotPresent(My.App.Card)", true)]
    [DataRow("IsTypeNotPresent(My.App.Card)", "IsTypePresent(My.App.Card)", true)]
    public void HalvesOfAnIfElsePairAreEachOthersOppositeWhateverIsJudgedFirst(string judgedFirst, string judgedLater, bool isPresent)
    {
        var outcomes = new ConcurrentDictionary<string, bool?>(StringComparer.Ordinal);
        bool? answer = isPresent;
        XamlConditionEvaluator? pinned = XamlMemberDispositionService.Pin(
            (condition, _) => condition.Contains("NotPresent") ? !answer : answer,
            outcomes
        );

        bool? first = pinned!(judgedFirst, _ => null);

        // A later answer changes nothing while the verdicts are kept.
        answer = !isPresent;
        bool? later = pinned(judgedLater, _ => null);

        Assert.AreEqual(judgedFirst.Contains("NotPresent") ? !isPresent : isPresent, first);
        Assert.AreEqual(judgedLater.Contains("NotPresent") ? !isPresent : isPresent, later);
    }

    [TestMethod]
    public void ConditionIsNotPinnedWithoutAnEvaluator()
    {
        Assert.IsNull(XamlMemberDispositionService.Pin(null, new ConcurrentDictionary<string, bool?>(StringComparer.Ordinal)));
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
