// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinUIDesigner.Markup;

/// <summary>
/// Covers how the answer to a condition becomes the verdict for the members under its namespace.
/// The rewrite of whole documents with this policy is covered by <see cref="ConditionalXamlRewriteTests"/>.
/// </summary>
[TestClass]
public sealed class ConditionalNamespacePolicyTests
{
    [TestMethod]
    [DataRow(true, "Keep")]
    [DataRow(false, "Drop")]
    [DataRow(null, "Leave")]
    public void VerdictFollowsTheAnswerToTheCondition(bool? answer, string expected)
    {
        XamlNamespaceUri uri = XamlNamespaceUri.Parse(TestXamlNamespaces.Conditional);
        var asked = new List<string>();

        XamlPrefixDisposition disposition = ConditionalNamespacePolicy.Classify(uri, condition =>
        {
            asked.Add(condition);
            return answer;
        });

        Assert.AreEqual(expected, disposition.ToString());

        // The evaluator receives the text after the '?' exactly as it was declared.
        CollectionAssert.AreEqual(new[] { TestXamlNamespaces.Condition }, asked);
    }

    [TestMethod]
    public void ConditionalNamespaceIsLeftAloneWithoutAnEvaluator()
    {
        XamlPrefixDisposition disposition = ConditionalNamespacePolicy.Classify(XamlNamespaceUri.Parse(TestXamlNamespaces.Conditional), evaluateCondition: null);

        Assert.AreEqual(XamlPrefixDisposition.Leave, disposition);
    }

    [TestMethod]
    [DataRow(XamlNamespaces.Presentation)]
    [DataRow("using:My.App")]
    [DataRow("http://uno.ui/android#using:My.Droid")]
    [DataRow("")]
    public void UnconditionalNamespaceIsLeftAloneWithoutAskingTheEvaluator(string namespaceUri)
    {
        XamlPrefixDisposition disposition = ConditionalNamespacePolicy.Classify(
            XamlNamespaceUri.Parse(namespaceUri),
            _ => throw new InvalidOperationException("An unconditional namespace has no condition to evaluate."));

        Assert.AreEqual(XamlPrefixDisposition.Leave, disposition);
    }

    [TestMethod]
    public void EmptyConditionIsHandedToTheEvaluator()
    {
        // "namespace?" is a conditional namespace whose predicate is empty; the evaluator knows what the XAML reader makes of it.
        var asked = new List<string>();

        XamlPrefixDisposition disposition = ConditionalNamespacePolicy.Classify(XamlNamespaceUri.Parse($"{XamlNamespaces.Presentation}?"), condition =>
        {
            asked.Add(condition);
            return true;
        });

        Assert.AreEqual(XamlPrefixDisposition.Keep, disposition);
        CollectionAssert.AreEqual(new[] { string.Empty }, asked);
    }

    [TestMethod]
    [DataRow(true, "Keep")]
    [DataRow(false, "Drop")]
    [DataRow(null, "Leave")]
    public void ConditionDecidesAPrefixThePlatformPolicyLeavesAlone(bool? answer, string expected)
    {
        XamlPrefixDisposition disposition = XamlNamespacePolicies.Classify(
            "c7",
            XamlNamespaceUri.Parse(TestXamlNamespaces.Conditional),
            isIgnorable: false,
            UnoPlatformPrefixes.DefaultTargetPlatform,
            _ => answer);

        Assert.AreEqual(expected, disposition.ToString());
    }

    [TestMethod]
    public void DocumentWithAConditionalNamespaceNeedsWorkOnceItsConditionsCanBeEvaluated()
    {
        string xaml = $"""<Page xmlns="{XamlNamespaces.Presentation}" xmlns:cond="using:My.App" xmlns:flag="{XamlNamespaces.Presentation}?cond:FeatureFlagCondition(NewExperience)"><flag:Border /></Page>""";
        XamlNamespaceScanResult scan = XamlNamespaceScanner.Scan(xaml);
        var resolved = new List<string?>();

        bool withoutEvaluator = XamlNamespacePolicies.MayAffect(scan, UnoPlatformPrefixes.DefaultTargetPlatform, evaluateCondition: null);
        bool unevaluated = XamlNamespacePolicies.MayAffect(scan, UnoPlatformPrefixes.DefaultTargetPlatform, (_, _) => null);
        bool evaluated = XamlNamespacePolicies.MayAffect(scan, UnoPlatformPrefixes.DefaultTargetPlatform, (condition, resolveNamespace) =>
        {
            // The prefix of a custom condition is resolved against the declarations of the document.
            resolved.Add(resolveNamespace(XamlCondition.Parse(condition).Prefix));
            return false;
        });

        Assert.IsFalse(withoutEvaluator);
        Assert.IsFalse(unevaluated);
        Assert.IsTrue(evaluated);
        CollectionAssert.AreEqual(new string?[] { "using:My.App" }, resolved);
    }
}
