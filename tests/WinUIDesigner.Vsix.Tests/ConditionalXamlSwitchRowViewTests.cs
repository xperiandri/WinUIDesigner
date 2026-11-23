// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinUIDesigner.Markup;
using WinUIDesigner.Platform;

/// <summary>
/// Covers the controls of a row of the Conditions list: only the user's switching forces an answer, also when it comes
/// through UI Automation, and an answer that is not known yet is not drawn as one that does not hold.
/// </summary>
[TestClass]
public sealed class ConditionalXamlSwitchRowViewTests
{
    private const string _Contract = "Windows.Foundation.UniversalApiContract";

    private const string _Page = $"""
        <Page xmlns="{XamlNamespaces.Presentation}"
              xmlns:cond="using:My.App"
              xmlns:c7="{TestXamlNamespaces.Conditional}"
              xmlns:newExperience="{XamlNamespaces.Presentation}?cond:FeatureFlagCondition(NewExperience)"
              xmlns:broken="{XamlNamespaces.Presentation}?IsApiContractPresent({_Contract},7.0)" />
        """;

    [STATestMethod]
    public void ShowingTheCurrentStateForcesNothing()
    {
        var changes = new List<string>();
        ConditionalXamlSwitchRowView view = CreateView("key", changes);

        // Every refresh of an open list writes the check box: none of it may become an override.
        view.Update(Row(XamlConditionOverrides.Empty, Holds, 0), canSwitch: true);
        Assert.AreEqual(true, view.CheckBox.IsChecked);
        view.Update(Row(XamlConditionOverrides.Empty, DoesNotHold, 0), canSwitch: true);
        Assert.AreEqual(false, view.CheckBox.IsChecked);
        view.Update(Row(XamlConditionOverrides.Empty, null, 0), canSwitch: true);
        Assert.IsNull(view.CheckBox.IsChecked);
        view.Update(Row(XamlConditionOverrides.Empty, Holds, 0), canSwitch: true);
        Assert.AreEqual(true, view.CheckBox.IsChecked);

        Assert.IsEmpty(changes);
    }

    [STATestMethod]
    public void SwitchingThroughUIAutomationForcesTheAnswer()
    {
        var changes = new List<string>();
        ConditionalXamlSwitchRow row = Row(XamlConditionOverrides.Empty, Holds, 0);
        ConditionalXamlSwitchRowView view = CreateView(row.Key, changes);
        view.Update(row, canSwitch: true);

        // A screen reader or a UI test toggles the check box without a click.
        Toggle(view.CheckBox);

        Assert.HasCount(1, changes);
        Assert.AreEqual($"{row.Key} holds=False", changes[0]);

        // The list shows the override it just stored, which is no new switching.
        view.Update(Row(XamlConditionOverrides.Empty.With(row.Key, holds: false), Holds, 0), canSwitch: true);
        Assert.HasCount(1, changes);
        Assert.AreEqual(false, view.CheckBox.IsChecked);

        Toggle(view.CheckBox);

        Assert.HasCount(2, changes);
        Assert.AreEqual($"{row.Key} holds=True", changes[1]);
        Assert.AreEqual(row.Title, view.Title);
    }

    [STATestMethod]
    public void AnAnswerThatIsNotKnownYetIsShownAsUndetermined()
    {
        var changes = new List<string>();
        ConditionalXamlSwitchRow row = Row(XamlConditionOverrides.Empty, null, 0);
        ConditionalXamlSwitchRowView view = CreateView(row.Key, changes);

        view.Update(row, canSwitch: true);

        // A cleared box would say that the condition does not hold, which the preview may contradict.
        Assert.IsNull(view.CheckBox.IsChecked);
        Assert.AreEqual("Not evaluated yet", AutomationProperties.GetItemStatus(view.CheckBox));
        Assert.Contains("Not evaluated yet.", AutomationProperties.GetHelpText(view.CheckBox));
        Assert.IsEmpty(changes);

        // The first click on an undetermined box clears it: the user wants the condition not to hold.
        Toggle(view.CheckBox);

        Assert.HasCount(1, changes);
        Assert.AreEqual($"{row.Key} holds=False", changes[0]);
    }

    [STATestMethod]
    public void AutomationSaysWhereTheAnswerComesFrom()
    {
        ConditionalXamlSwitchRowView view = CreateView("key", []);
        ConditionalXamlSwitchRow row = Row(XamlConditionOverrides.Empty, Holds, 0);

        view.Update(row, canSwitch: true);
        Assert.AreEqual("Evaluated", AutomationProperties.GetItemStatus(view.CheckBox));

        view.Update(Row(XamlConditionOverrides.Empty.With(row.Key, holds: false), Holds, 0), canSwitch: true);
        Assert.AreEqual("Overridden", AutomationProperties.GetItemStatus(view.CheckBox));
        Assert.AreEqual(row.Title, AutomationProperties.GetName(view.CheckBox));
    }

    [STATestMethod]
    public void ARowThatCannotBeSwitchedCannotBeToggled()
    {
        var changes = new List<string>();
        ConditionalXamlSwitchRowView view = CreateView("key", changes);

        // A malformed predicate is skipped whatever the user wants.
        view.Update(Row(XamlConditionOverrides.Empty, Holds, 2), canSwitch: true);
        Assert.IsFalse(view.CheckBox.IsEnabled);
        Assert.ThrowsExactly<ElementNotEnabledException>(() => Toggle(view.CheckBox));

        // A screen reader is told by the status, as by the help text, that it cannot be switched, not that it was evaluated.
        Assert.AreEqual("Cannot be switched", AutomationProperties.GetItemStatus(view.CheckBox));
        Assert.Contains("Cannot be switched", AutomationProperties.GetHelpText(view.CheckBox));

        // Nothing can be switched while the options cannot be sent to the design surface.
        view.Update(Row(XamlConditionOverrides.Empty, Holds, 0), canSwitch: false);
        Assert.IsFalse(view.CheckBox.IsEnabled);
        Assert.IsFalse(view.ResetButton.IsEnabled);
        Assert.ThrowsExactly<ElementNotEnabledException>(() => Toggle(view.CheckBox));

        Assert.IsEmpty(changes);
    }

    [STATestMethod]
    public void ResetIsOfferedForAnOverriddenQuestionOnly()
    {
        var resets = new List<ConditionalXamlSwitchRowView>();
        ConditionalXamlSwitchRowView view = CreateView("key", [], resets);
        ConditionalXamlSwitchRow row = Row(XamlConditionOverrides.Empty, Holds, 0);

        view.Update(row, canSwitch: true);
        Assert.AreEqual(Visibility.Collapsed, view.ResetButton.Visibility);

        view.Update(Row(XamlConditionOverrides.Empty.With(row.Key, holds: false), Holds, 0), canSwitch: true);
        Assert.AreEqual(Visibility.Visible, view.ResetButton.Visibility);
        Assert.AreEqual($"Reset {row.Title}", AutomationProperties.GetName(view.ResetButton));

        view.ResetButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

        Assert.HasCount(1, resets);
        Assert.AreSame(view, resets[0]);
    }

    private static ConditionalXamlSwitchRowView CreateView(string key, List<string> changes, List<ConditionalXamlSwitchRowView>? resets = null)
    {
        return new ConditionalXamlSwitchRowView(
            key,
            (view, holds) => changes.Add($"{view.Key} holds={holds}"),
            view => resets?.Add(view)
        );
    }

    // The rows of the page in the order of their declarations: the contract, the feature flag, the malformed predicate.
    private static ConditionalXamlSwitchRow Row(XamlConditionOverrides overrides, Func<XamlConditionQuery, bool?>? evaluate, int index)
    {
        IReadOnlyList<XamlConditionSwitch> switches = XamlConditionSwitches.Collect(XamlNamespaceScanner.Scan(_Page));
        return ConditionalXamlSwitchRow.Create(switches, overrides, evaluate)[index];
    }

    private static bool? Holds(XamlConditionQuery query) => true;

    private static bool? DoesNotHold(XamlConditionQuery query) => false;

    private static void Toggle(CheckBox checkBox)
    {
        var provider = (IToggleProvider)UIElementAutomationPeer.CreatePeerForElement(checkBox);
        provider.Toggle();
    }
}
