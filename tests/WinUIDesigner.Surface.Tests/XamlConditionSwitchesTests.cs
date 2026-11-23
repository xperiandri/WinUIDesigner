// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinUIDesigner.Markup;

/// <summary>
/// Covers the list of questions the designer offers to switch: one entry per question of a document, with the
/// prefixes that ask it, built from the namespace declarations of the document's text.
/// </summary>
[TestClass]
public sealed class XamlConditionSwitchesTests
{
    private const string _Contract = "Windows.Foundation.UniversalApiContract";

    // The declarations of samples/WinUIDesignerSampleApp/ConditionalXamlPage.xaml.
    private const string _SamplePage = $"""
        <Page
            x:Class="WinUIDesignerSampleApp.ConditionalXamlPage"
            xmlns="{XamlNamespaces.Presentation}"
            xmlns:x="{XamlNamespaces.XamlLanguage}"
            xmlns:cond="using:WinUIDesignerSampleApp.Conditions"
            xmlns:contract7NotPresent="{XamlNamespaces.Presentation}?IsApiContractNotPresent({_Contract},7)"
            xmlns:contract7Present="{XamlNamespaces.Presentation}?IsApiContractPresent({_Contract},7)"
            xmlns:contract99NotPresent="{XamlNamespaces.Presentation}?IsApiContractNotPresent({_Contract},99)"
            xmlns:contract99Present="{XamlNamespaces.Presentation}?IsApiContractPresent({_Contract},99)"
            xmlns:legacyMode="{XamlNamespaces.Presentation}?cond:FeatureFlagCondition(LegacyMode)"
            xmlns:newExperience="{XamlNamespaces.Presentation}?cond:FeatureFlagCondition(NewExperience)">
            <StackPanel>
                <contract7Present:TextBlock Text="Contract 7 is present." />
                <contract7NotPresent:TextBlock Text="Contract 7 is not present." />
            </StackPanel>
        </Page>
        """;

    [TestMethod]
    public void SamplePageHasOneSwitchPerQuestion()
    {
        IReadOnlyList<XamlConditionSwitch> switches = Collect(_SamplePage);

        CollectionAssert.AreEqual(
            new[]
            {
                $"IsApiContractPresent({_Contract},7)",
                $"IsApiContractPresent({_Contract},99)",
                "cond:FeatureFlagCondition(LegacyMode)",
                "cond:FeatureFlagCondition(NewExperience)",
            },
            switches.Select(entry => entry.DisplayText).ToArray()
        );
        CollectionAssert.AreEqual(
            new[]
            {
                $"IsApiContractPresent({_Contract},7)",
                $"IsApiContractPresent({_Contract},99)",
                "using:WinUIDesignerSampleApp.Conditions|FeatureFlagCondition(LegacyMode)",
                "using:WinUIDesignerSampleApp.Conditions|FeatureFlagCondition(NewExperience)",
            },
            switches.Select(entry => entry.Key).ToArray()
        );

        // The prefixes in the order they are declared, and which form of the question each one asks.
        CollectionAssert.AreEqual(new[] { "contract7NotPresent!", "contract7Present" }, Prefixes(switches[0]));
        CollectionAssert.AreEqual(new[] { "contract99NotPresent!", "contract99Present" }, Prefixes(switches[1]));
        CollectionAssert.AreEqual(new[] { "legacyMode" }, Prefixes(switches[2]));
        CollectionAssert.AreEqual(new[] { "newExperience" }, Prefixes(switches[3]));
        Assert.IsTrue(switches.All(entry => entry.IsSwitchable && entry.Error is null));

        // The value of a switch is the answer to the positive form of its question.
        Assert.IsFalse(switches[0].Query.Condition.IsNegated);
        Assert.AreEqual(XamlConditionKind.ApiContract, switches[0].Query.Condition.Kind);
        Assert.AreEqual("WinUIDesignerSampleApp.Conditions.FeatureFlagCondition", switches[3].Query.TypeName);
    }

    [TestMethod]
    public void PrefixKnowsWhenItsMembersApply()
    {
        IReadOnlyList<XamlConditionSwitch> switches = Collect(_SamplePage);
        XamlConditionSwitchPrefix notPresent = switches[0].Prefixes[0];
        XamlConditionSwitchPrefix present = switches[0].Prefixes[1];

        Assert.IsTrue(present.Applies(holds: true));
        Assert.IsFalse(present.Applies(holds: false));
        Assert.IsFalse(notPresent.Applies(holds: true));
        Assert.IsTrue(notPresent.Applies(holds: false));
    }

    [TestMethod]
    public void QuestionWrittenOnlyInItsNegativeFormIsShownInItsPositiveForm()
    {
        IReadOnlyList<XamlConditionSwitch> switches = Collect($"""
            <Grid xmlns="{XamlNamespaces.Presentation}"
                  xmlns:c8n="{XamlNamespaces.Presentation}?IsApiContractNotPresent( {_Contract} , 8 )"
                  xmlns:noUri="{XamlNamespaces.Presentation}?IsTypeNotPresent(Windows.Foundation.Uri)"
                  xmlns:noRadius="{XamlNamespaces.Presentation}?IsPropertyNotPresent(Microsoft.UI.Xaml.Controls.Control,CornerRadius)" />
            """);

        CollectionAssert.AreEqual(
            new[]
            {
                $"IsApiContractPresent({_Contract},8)",
                "IsTypePresent(Windows.Foundation.Uri)",
                "IsPropertyPresent(Microsoft.UI.Xaml.Controls.Control,CornerRadius)",
            },
            switches.Select(entry => entry.DisplayText).ToArray()
        );
        Assert.IsTrue(switches.All(entry => !entry.Query.Condition.IsNegated && entry.Prefixes.Single().IsNegated));
        Assert.IsTrue(switches.All(entry => entry.Key == entry.DisplayText));
    }

    [TestMethod]
    public void SpellingsOfOneQuestionShareTheirSwitch()
    {
        IReadOnlyList<XamlConditionSwitch> switches = Collect($"""
            <Grid xmlns="{XamlNamespaces.Presentation}"
                  xmlns:p="{XamlNamespaces.Presentation}"
                  xmlns:a="{XamlNamespaces.Presentation}?IsApiContractPresent({_Contract},7)"
                  xmlns:b="{XamlNamespaces.Presentation}?IsApiContractPresent( {_Contract} ,7 )"
                  xmlns:c="{XamlNamespaces.Presentation}?p:IsApiContractNotPresent({_Contract},7)">
              <Border xmlns:a="{XamlNamespaces.Presentation}?IsApiContractPresent({_Contract},7)" />
            </Grid>
            """);

        XamlConditionSwitch entry = switches.Single();
        Assert.AreEqual($"IsApiContractPresent({_Contract},7)", entry.DisplayText);
        CollectionAssert.AreEqual(new[] { "a", "b", "c!" }, Prefixes(entry), "A prefix declared again with the same question is listed once.");
    }

    [TestMethod]
    public void MalformedPredicateIsListedButCannotBeSwitched()
    {
        IReadOnlyList<XamlConditionSwitch> switches = Collect($"""
            <Grid xmlns="{XamlNamespaces.Presentation}"
                  xmlns:bad="{XamlNamespaces.Presentation}?IsApiContractPresent({_Contract},7.0)"
                  xmlns:again="{XamlNamespaces.Presentation}?IsApiContractPresent({_Contract},7.0)"
                  xmlns:unknown="{XamlNamespaces.Presentation}?NoSuchMethod"
                  xmlns:good="{XamlNamespaces.Presentation}?IsApiContractPresent({_Contract},7)" />
            """);

        Assert.HasCount(3, switches);
        XamlConditionSwitch bad = switches[0];
        Assert.AreEqual($"IsApiContractPresent({_Contract},7.0)", bad.DisplayText);
        CollectionAssert.AreEqual(new[] { "bad", "again" }, Prefixes(bad));
        Assert.IsFalse(bad.IsSwitchable);
        Assert.IsNotNull(bad.Error);
        Assert.Contains("not a contract version", bad.Error);

        XamlConditionSwitch unknown = switches[1];
        Assert.IsFalse(unknown.IsSwitchable);
        Assert.IsNotNull(unknown.Error);
        Assert.Contains("argument list is missing", unknown.Error);
        Assert.IsTrue(switches[2].IsSwitchable);
    }

    [TestMethod]
    public void UnconditionalNamespacesAreNotListed()
    {
        Assert.IsEmpty(Collect($"""<Grid xmlns="{XamlNamespaces.Presentation}" xmlns:empty="{XamlNamespaces.Presentation}?" xmlns:local="using:My.App" />"""));
        Assert.IsEmpty(Collect(string.Empty));
        Assert.IsEmpty(XamlConditionSwitches.Collect(null));
    }

    [TestMethod]
    public void CustomConditionResolvesThroughEveryBindingOfItsPrefix()
    {
        IReadOnlyList<XamlConditionSwitch> switches = Collect($"""
            <Grid xmlns="{XamlNamespaces.Presentation}"
                  xmlns:cond="using:My.App"
                  xmlns:flag="{XamlNamespaces.Presentation}?cond:FeatureFlagCondition(On)"
                  xmlns:other="{XamlNamespaces.Presentation}?nope:FeatureFlagCondition(On)">
              <Border xmlns:cond="using:My.Other" />
            </Grid>
            """);

        CollectionAssert.AreEqual(
            new[]
            {
                "using:My.App|FeatureFlagCondition(On)",
                "using:My.Other|FeatureFlagCondition(On)",
                "?nope|FeatureFlagCondition(On)",
            },
            switches.Select(entry => entry.Key).ToArray()
        );
        CollectionAssert.AreEqual(new[] { "flag" }, Prefixes(switches[0]));
        CollectionAssert.AreEqual(new[] { "flag" }, Prefixes(switches[1]));

        // An undeclared prefix is not an error of the predicate: forcing it to hold shows its members.
        Assert.IsTrue(switches[2].IsSwitchable);
        Assert.IsNull(switches[2].Query.PredicateNamespace);
    }

    [TestMethod]
    public void CustomArgumentIsKeptAsWritten()
    {
        IReadOnlyList<XamlConditionSwitch> switches = Collect($"""
            <Grid xmlns="{XamlNamespaces.Presentation}" xmlns:cond="using:My.App"
                  xmlns:f="{XamlNamespaces.Presentation}?cond:Flag( A;B=C,D%E (nested) )" />
            """);

        XamlConditionSwitch entry = switches.Single();
        Assert.AreEqual("cond:Flag( A;B=C,D%E (nested) )", entry.DisplayText);
        Assert.AreEqual("using:My.App|Flag( A;B=C,D%E (nested) )", entry.Key);
        Assert.AreEqual(" A;B=C,D%E (nested) ", entry.Query.Condition.Argument);
    }

    [TestMethod]
    public void MalformedXamlYieldsTheSwitchesDeclaredBeforeTheError()
    {
        IReadOnlyList<XamlConditionSwitch> switches = Collect($"""
            <Grid xmlns="{XamlNamespaces.Presentation}" xmlns:c7="{XamlNamespaces.Presentation}?IsApiContractPresent({_Contract},7)">
              <Border>
            </Grid>
            """);

        Assert.AreEqual($"IsApiContractPresent({_Contract},7)", switches.Single().DisplayText);
    }

    private static IReadOnlyList<XamlConditionSwitch> Collect(string xaml)
    {
        return XamlConditionSwitches.Collect(XamlNamespaceScanner.Scan(xaml));
    }

    // A prefix that asks the negative form of the question is marked with '!'.
    private static string[] Prefixes(XamlConditionSwitch entry)
    {
        return entry.Prefixes.Select(prefix => $"{prefix.Prefix}{(prefix.IsNegated ? "!" : string.Empty)}").ToArray();
    }
}
