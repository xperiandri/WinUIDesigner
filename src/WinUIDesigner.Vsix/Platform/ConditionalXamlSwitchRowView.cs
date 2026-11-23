// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;

namespace WinUIDesigner.Platform;

/// <summary>
/// The controls of one row of the Conditions list: a check box for the answer, the prefixes the question decides,
/// where the answer comes from, and a button that lets the question follow its evaluation again.
/// </summary>
/// <remarks>
/// The row does not know the drop-down: what the user does is handed out as delegates, so that the row can be tested
/// without the designer. Only the user's switching reports a change; showing the current state does not.
/// </remarks>
internal sealed class ConditionalXamlSwitchRowView
{
    private readonly Action<ConditionalXamlSwitchRowView, bool> _switched;
    private readonly TextBlock _title;
    private readonly TextBlock _branches;
    private readonly TextBlock _status;

    // Set while Update writes the check box, so that only the user's switching reports a change.
    private bool _updating;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConditionalXamlSwitchRowView"/> class.
    /// </summary>
    /// <param name="key">The key of the question the row stands for.</param>
    /// <param name="switched">Called when the user checks or clears the check box, with the answer the user wants.</param>
    /// <param name="reset">Called when the user lets the question follow its evaluation again.</param>
    public ConditionalXamlSwitchRowView(string key, Action<ConditionalXamlSwitchRowView, bool> switched, Action<ConditionalXamlSwitchRowView> reset)
    {
        Key = key;
        _switched = switched;

        // The text of the list keeps the theme's color whatever the check box style does with its content.
        _title = new TextBlock { TextWrapping = TextWrapping.Wrap };
        _title.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.CommandBarTextActiveBrushKey);
        CheckBox = new CheckBox { Content = _title, VerticalContentAlignment = VerticalAlignment.Top };
        CheckBox.SetResourceReference(FrameworkElement.StyleProperty, VsResourceKeys.CheckBoxStyleKey);

        // Checked and Unchecked rather than Click: switching through UI Automation (screen readers, UI tests)
        // toggles the check box without a click.
        CheckBox.Checked += (_, _) => OnSwitched();
        CheckBox.Unchecked += (_, _) => OnSwitched();

        _branches = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(20, 2, 0, 0) };
        _status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(20, 1, 0, 0) };

        ResetButton = new Button
        {
            Content = "Reset",
            Margin = new Thickness(12, 0, 0, 0),
            Padding = new Thickness(8, 1, 8, 1),
            VerticalAlignment = VerticalAlignment.Top,
        };
        ResetButton.SetResourceReference(FrameworkElement.StyleProperty, VsResourceKeys.ButtonStyleKey);
        ResetButton.Click += (_, _) => reset(this);

        var details = new StackPanel();
        details.Children.Add(CheckBox);
        details.Children.Add(_branches);
        details.Children.Add(_status);

        var root = new Grid { Margin = new Thickness(0, 4, 0, 6) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(ResetButton, 1);
        root.Children.Add(details);
        root.Children.Add(ResetButton);
        Root = root;
    }

    /// <summary>
    /// Gets the key of the question the row stands for.
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// Gets the text of the question as the row shows it, for the log.
    /// </summary>
    public string Title { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the element to put into the list.
    /// </summary>
    public FrameworkElement Root { get; }

    /// <summary>
    /// Gets the check box: checked when the question holds.
    /// </summary>
    public CheckBox CheckBox { get; }

    /// <summary>
    /// Gets the button that lets the question follow its evaluation again; it is shown for an overridden question.
    /// </summary>
    public Button ResetButton { get; }

    /// <summary>
    /// Shows the current state of the question. This does not report a change to the user's switching.
    /// </summary>
    /// <param name="row">What the question evaluates to, whether the user forces it, and the texts to show.</param>
    /// <param name="canSwitch">Whether the user can force an answer at all.</param>
    public void Update(ConditionalXamlSwitchRow row, bool canSwitch)
    {
        Title = row.Title;
        _title.Text = row.Title;
        _updating = true;
        try
        {
            CheckBox.IsChecked = row.Value == true;
        }
        finally
        {
            _updating = false;
        }

        CheckBox.IsEnabled = row.IsSwitchable && canSwitch;
        _title.Opacity = CheckBox.IsEnabled ? 1.0 : 0.6;
        AutomationProperties.SetName(CheckBox, row.Title);
        AutomationProperties.SetHelpText(CheckBox, row.Branches.Length == 0 ? row.Status : $"{row.Branches}. {row.Status}");
        AutomationProperties.SetItemStatus(CheckBox, row.IsOverridden ? "Overridden" : "Evaluated");

        _branches.Text = row.Branches;
        _branches.Visibility = row.Branches.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        _status.Text = row.Status;
        _status.FontWeight = row.IsOverridden ? FontWeights.SemiBold : FontWeights.Normal;

        ResetButton.Visibility = row.IsOverridden ? Visibility.Visible : Visibility.Collapsed;
        ResetButton.IsEnabled = canSwitch;
        AutomationProperties.SetName(ResetButton, $"Reset {row.Title}");
        AutomationProperties.SetHelpText(ResetButton, "Lets this condition follow its evaluation again.");
    }

    private void OnSwitched()
    {
        if (!_updating)
        {
            _switched(this, CheckBox.IsChecked == true);
        }
    }
}
