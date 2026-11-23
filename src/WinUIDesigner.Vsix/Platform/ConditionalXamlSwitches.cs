// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.VisualStudio.DesignTools.Markup.DocumentModel;
using Microsoft.VisualStudio.DesignTools.SurfaceDesigner.Documents.Project;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using WinUIDesigner.DesignTime;
using WinUIDesigner.Markup;
using IMarkupTextBuffer = Microsoft.VisualStudio.DesignTools.Utility.Text.IMarkupTextBuffer;
using MarkupTextChangedEventArgs = Microsoft.VisualStudio.DesignTools.Utility.Text.TextChangedEventArgs;
using SceneScrollViewer = Microsoft.VisualStudio.DesignTools.SurfaceDesigner.Views.SceneScrollViewer;

namespace WinUIDesigner.Platform;

/// <summary>
/// The "Conditions" drop-down of the WinUI designer: it lists the questions the conditional XAML namespaces of the
/// document ask and lets the user force each answer, so that the preview shows the other branch of a condition.
/// </summary>
/// <remarks>
/// <para>
/// Visual Studio's designer pane has no extension point for toolbar items. The drop-down button is placed into the
/// empty column after the last separator of the device toolbar (<c>SceneScrollViewer.DeviceToolBar</c>, a
/// <see cref="Border"/> whose child is a <see cref="Grid"/>); without a device toolbar, into a strip above the
/// content of the scene scroll viewer; otherwise not at all. The list itself is a <see cref="Popup"/>, a window of
/// its own, because the design surface is a window of another process that would cover anything drawn over it.
/// </para>
/// <para>
/// An override applies to every document of the surface process and is kept with the design-time options; a
/// change restarts the surface process, which reloads every designer of the project. The list is built from the
/// text of the document each time it opens, and the button follows edits of the text.
/// </para>
/// <para>
/// Nothing here may break the designer: every failure is logged, and after a few failures the drop-down turns
/// itself off for its designer.
/// </para>
/// </remarks>
internal sealed class ConditionalXamlSwitches : IDisposable
{
    private const string _Category = "Platform";
    private const int _MaxFailures = 3;
    private const string _AutomationIdPrefix = "WinUIDesignerConditionalXaml";

    private static readonly TimeSpan _TextChangeDelay = TimeSpan.FromMilliseconds(500);
    private static readonly PropertyInfo? _DeviceToolBarProperty = typeof(SceneScrollViewer).GetProperty("DeviceToolBar", BindingFlags.Public | BindingFlags.Instance);
    private static readonly object _InstancesGate = new();
    private static readonly List<WeakReference<ConditionalXamlSwitches>> _Instances = [];
    private static volatile bool _isEnabled = true;
    private static int _placementReported;

    private readonly Func<IDocumentRoot?> _getDocument;
    private readonly Func<IProjectContext?> _getProject;
    private readonly Dispatcher _dispatcher;
    private readonly ToggleButton _button;
    private readonly TextBlock _buttonText;
    private readonly Popup _popup;
    private readonly Border _popupRoot;
    private readonly StackPanel _rowsPanel;
    private readonly TextBlock _emptyText;
    private readonly TextBlock _unavailableText;
    private readonly TextBlock _summaryText;
    private readonly Button _resetAllButton;
    private readonly DispatcherTimer _textChangedTimer;
    private readonly List<ConditionalXamlSwitchRowView> _rowViews = [];

    private IMarkupTextBuffer? _buffer;
    private EventHandler<MarkupTextChangedEventArgs>? _textChangedHandler;
    private IDesignTimeTargetProvider? _provider;
    private EventHandler? _optionsChangedHandler;
    private IReadOnlyList<XamlConditionSwitch> _switches = [];
    private int _scannedVersion = -1;
    private int _scannedLength = -1;
    private int _failures;
    private bool _isDisposed;

    private ConditionalXamlSwitches(Func<IDocumentRoot?> getDocument, Func<IProjectContext?> getProject)
    {
        _getDocument = getDocument;
        _getProject = getProject;
        _dispatcher = Dispatcher.CurrentDispatcher;

        _buttonText = new TextBlock { Text = ConditionalXamlSwitchRow.FormatButtonLabel(0), VerticalAlignment = VerticalAlignment.Center };
        var chevron = new Path
        {
            Data = Geometry.Parse("M 0,0 L 3.5,4 L 7,0 Z"),
            Width = 7,
            Height = 4,
            Margin = new Thickness(6, 1, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };

        _button = new ToggleButton
        {
            Height = 22,
            Padding = new Thickness(6, 0, 6, 0),
            Margin = new Thickness(0, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            Template = CreateToolbarButtonTemplate(),
            Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { _buttonText, chevron } },
            ToolTip = "Conditional XAML: choose whether each condition of this document holds in the preview",
            Visibility = Visibility.Collapsed,
        };
        _button.SetResourceReference(Control.ForegroundProperty, EnvironmentColors.CommandBarTextActiveBrushKey);
        chevron.SetBinding(Shape.FillProperty, new Binding(nameof(Control.Foreground)) { Source = _button });
        AutomationProperties.SetName(_button, "Conditional XAML conditions");
        AutomationProperties.SetHelpText(_button, "Lists the conditions of the conditional XAML namespaces of this document and lets you choose whether each one holds in the preview.");
        AutomationProperties.SetAutomationId(_button, $"{_AutomationIdPrefix}Button");

        _rowsPanel = new StackPanel();
        _emptyText = new TextBlock
        {
            Text = "This document declares no conditional XAML namespace.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 4),
        };
        _unavailableText = new TextBlock
        {
            Text = "Conditions cannot be switched: the design-time options cannot be sent to the design surface. The WinUI Designer log says why.",
            TextWrapping = TextWrapping.Wrap,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6),
        };
        _summaryText = new TextBlock { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        _resetAllButton = new Button
        {
            Content = "Reset all",
            Margin = new Thickness(12, 0, 0, 0),
            Padding = new Thickness(8, 1, 8, 1),
            VerticalAlignment = VerticalAlignment.Center,
        };
        _resetAllButton.SetResourceReference(FrameworkElement.StyleProperty, VsResourceKeys.ButtonStyleKey);
        AutomationProperties.SetName(_resetAllButton, "Reset all conditions");
        AutomationProperties.SetHelpText(_resetAllButton, "Lets every condition of the project follow its evaluation again.");
        AutomationProperties.SetAutomationId(_resetAllButton, $"{_AutomationIdPrefix}ResetAll");
        _resetAllButton.Click += (_, _) => Guard("resetting all conditions", ResetAllOverrides);

        _popupRoot = CreatePopupContent();
        _popup = new Popup
        {
            Child = _popupRoot,
            PlacementTarget = _button,
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
            Focusable = false,
            PopupAnimation = PopupAnimation.None,
        };

        _textChangedTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher) { Interval = _TextChangeDelay };
        _textChangedTimer.Tick += (_, _) =>
        {
            _textChangedTimer.Stop();
            Guard("following an edit of the document", Refresh);
        };

        _button.Checked += (_, _) => Guard("opening the conditions", OpenPopup);
        _button.Unchecked += (_, _) => Guard("closing the conditions", () => _popup.IsOpen = false);
        _button.PreviewKeyDown += OnButtonPreviewKeyDown;
        _button.Loaded += (_, _) => Guard("showing the conditions button", Refresh);
        _button.Unloaded += (_, _) => Guard("closing the conditions", ClosePopup);
        _button.IsVisibleChanged += (_, _) =>
        {
            if (!_button.IsVisible)
            {
                Guard("closing the conditions", ClosePopup);
            }
        };

        _popup.Opened += (_, _) => Guard("opening the conditions", OnPopupOpened);
        _popup.Closed += (_, _) => Guard("closing the conditions", OnPopupClosed);
    }

    /// <summary>
    /// Gets or sets whether the drop-down is shown, as chosen in the options of the extension. Turning it off hides
    /// every drop-down at once; turning it on shows it again at once in the designers that had it. A designer that
    /// was opened while the option was off has no drop-down, so it gets one when it is opened again.
    /// </summary>
    public static bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value)
            {
                return;
            }

            _isEnabled = value;
            foreach (ConditionalXamlSwitches switches in GetLiveInstances())
            {
                _ = switches._dispatcher.BeginInvoke(new Action(() => switches.Guard("applying the options", switches.Refresh)));
            }
        }
    }

    /// <summary>
    /// Adds the drop-down to the scene scroll viewer of a designer. Never throws.
    /// </summary>
    /// <param name="scrollViewer">The scroll viewer Visual Studio created for the designer.</param>
    /// <param name="getDocument">Returns the document the designer shows.</param>
    /// <param name="getProject">Returns the project context of the document.</param>
    /// <returns>The drop-down, to be disposed with the designer, or <see langword="null"/> when there is none.</returns>
    public static ConditionalXamlSwitches? TryAttach(SceneScrollViewer scrollViewer, Func<IDocumentRoot?> getDocument, Func<IProjectContext?> getProject)
    {
        // The option is off: the designer gets no drop-down.
        if (!_isEnabled)
        {
            return null;
        }

        ConditionalXamlSwitches? switches = null;
        try
        {
            switches = new ConditionalXamlSwitches(getDocument, getProject);
            string? placement = switches.Place(scrollViewer);

            // There is no place for the button.
            if (placement is null)
            {
                ReportPlacementOnce(isPlaced: false, "Conditional XAML switches: the designer has neither a device toolbar nor content to put them above; the designer works without them.");
                switches.Dispose();
                return null;
            }

            ReportPlacementOnce(isPlaced: true, $"Conditional XAML switches are shown {placement}.");
            WinUIDesignerLogger.LogTrace(_Category, $"Conditional XAML switches: added to a designer, {placement}.");
            lock (_InstancesGate)
            {
                _Instances.RemoveAll(reference => !reference.TryGetTarget(out _));
                _Instances.Add(new WeakReference<ConditionalXamlSwitches>(switches));
            }

            // The document may not be readable yet; the button is brought up to date again once it is loaded.
            switches.Guard("showing the conditions button", switches.Refresh);
            return switches;
        }
        catch (Exception exception)
        {
            // Anything that goes wrong while the button is added leaves the designer without it.
            WinUIDesignerLogger.LogWarning(_Category, "Conditional XAML switches could not be added to the designer; the designer works without them.", exception);
            try
            {
                switches?.Dispose();
            }
            catch (Exception)
            {
                // Already reported.
            }

            return null;
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        try
        {
            _textChangedTimer.Stop();
            _popup.IsOpen = false;
            _button.Visibility = Visibility.Collapsed;
            SubscribeToBuffer(null);
            SubscribeToProvider(null);
            lock (_InstancesGate)
            {
                _Instances.RemoveAll(reference => !reference.TryGetTarget(out ConditionalXamlSwitches? target) || ReferenceEquals(target, this));
            }
        }
        catch (Exception exception)
        {
            WinUIDesignerLogger.LogTrace(_Category, $"Releasing the conditional XAML switches failed: {exception}");
        }
    }

    private static List<ConditionalXamlSwitches> GetLiveInstances()
    {
        var instances = new List<ConditionalXamlSwitches>();
        lock (_InstancesGate)
        {
            _Instances.RemoveAll(reference => !reference.TryGetTarget(out _));
            foreach (WeakReference<ConditionalXamlSwitches> reference in _Instances)
            {
                if (reference.TryGetTarget(out ConditionalXamlSwitches? instance))
                {
                    instances.Add(instance);
                }
            }
        }

        return instances;
    }

    private static void ReportPlacementOnce(bool isPlaced, string message)
    {
        // Every designer is placed the same way, so the log says it once per Visual Studio process.
        if (System.Threading.Interlocked.Exchange(ref _placementReported, 1) != 0)
        {
            return;
        }

        if (isPlaced)
        {
            WinUIDesignerLogger.LogInformation(_Category, message);
        }
        else
        {
            WinUIDesignerLogger.LogWarning(_Category, message);
        }
    }

    private static ControlTemplate CreateToolbarButtonTemplate()
    {
        const string chromeName = "Chrome";

        var chrome = new FrameworkElementFactory(typeof(Border), chromeName);
        chrome.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        chrome.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        chrome.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
        chrome.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        chrome.SetValue(UIElement.SnapsToDevicePixelsProperty, true);

        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
        chrome.AppendChild(presenter);

        var template = new ControlTemplate(typeof(ToggleButton)) { VisualTree = chrome };

        // Later triggers win, so an open drop-down looks pressed whether or not the mouse is over it.
        template.Triggers.Add(CreateChromeTrigger(UIElement.IsKeyboardFocusedProperty, chromeName, backgroundKey: null, EnvironmentColors.CommandBarSelectedBorderBrushKey));
        template.Triggers.Add(CreateChromeTrigger(UIElement.IsMouseOverProperty, chromeName, EnvironmentColors.CommandBarMouseOverBackgroundBeginBrushKey, EnvironmentColors.CommandBarSelectedBorderBrushKey));
        template.Triggers.Add(CreateChromeTrigger(ToggleButton.IsCheckedProperty, chromeName, EnvironmentColors.CommandBarMouseDownBackgroundBeginBrushKey, EnvironmentColors.CommandBarSelectedBorderBrushKey));
        return template;
    }

    private static Trigger CreateChromeTrigger(DependencyProperty property, string chromeName, object? backgroundKey, object borderKey)
    {
        var trigger = new Trigger { Property = property, Value = true };
        if (backgroundKey is not null)
        {
            trigger.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension(backgroundKey), chromeName));
        }

        trigger.Setters.Add(new Setter(Border.BorderBrushProperty, new DynamicResourceExtension(borderKey), chromeName));
        return trigger;
    }

    private Border CreatePopupContent()
    {
        var header = new TextBlock
        {
            Text = "Conditional XAML",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 2),
        };
        var description = new TextBlock
        {
            Text = "Choose whether each condition holds in the preview. A change applies to every document of the project and reloads the designer.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        };

        var rows = new ScrollViewer
        {
            Content = _rowsPanel,
            MaxHeight = 360,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false,
        };
        rows.SetResourceReference(FrameworkElement.StyleProperty, VsResourceKeys.ScrollViewerStyleKey);

        var separator = new Border { Height = 1, Margin = new Thickness(0, 8, 0, 8) };
        separator.SetResourceReference(Border.BackgroundProperty, EnvironmentColors.CommandBarMenuSeparatorBrushKey);

        var footer = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(_resetAllButton, Dock.Right);
        footer.Children.Add(_resetAllButton);
        footer.Children.Add(_summaryText);

        var content = new StackPanel();
        content.Children.Add(header);
        content.Children.Add(description);
        content.Children.Add(_unavailableText);
        content.Children.Add(_emptyText);
        content.Children.Add(rows);
        content.Children.Add(separator);
        content.Children.Add(footer);

        var root = new Border
        {
            Child = content,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 10, 12, 10),
            MinWidth = 360,
            MaxWidth = 640,
        };
        root.SetResourceReference(Border.BackgroundProperty, EnvironmentColors.CommandBarMenuBackgroundGradientBeginBrushKey);
        root.SetResourceReference(Border.BorderBrushProperty, EnvironmentColors.CommandBarMenuBorderBrushKey);
        root.SetResourceReference(TextElement.ForegroundProperty, EnvironmentColors.CommandBarTextActiveBrushKey);
        root.SetResourceReference(TextElement.FontFamilyProperty, VsFonts.EnvironmentFontFamilyKey);
        root.SetResourceReference(TextElement.FontSizeProperty, VsFonts.EnvironmentFontSizeKey);
        KeyboardNavigation.SetTabNavigation(root, KeyboardNavigationMode.Cycle);
        KeyboardNavigation.SetDirectionalNavigation(root, KeyboardNavigationMode.Cycle);
        AutomationProperties.SetName(root, "Conditional XAML conditions");
        AutomationProperties.SetAutomationId(root, $"{_AutomationIdPrefix}List");
        root.PreviewKeyDown += OnPopupPreviewKeyDown;
        return root;
    }

    private string? Place(SceneScrollViewer scrollViewer)
    {
        // The device toolbar is the first choice.
        try
        {
            if (TryPlaceInDeviceToolBar(scrollViewer))
            {
                return "in the device toolbar of the designer";
            }
        }
        catch (Exception exception)
        {
            // A Visual Studio without the device toolbar of this version; the strip is the next best place.
            WinUIDesignerLogger.LogTrace(_Category, $"The device toolbar cannot hold the conditional XAML switches: {exception}");
        }

        // A strip above the content is the second.
        try
        {
            if (TryPlaceInStrip(scrollViewer))
            {
                return "in a strip above the design surface";
            }
        }
        catch (Exception exception)
        {
            // The scroll viewer has no content to put the strip above.
            WinUIDesignerLogger.LogTrace(_Category, $"The scene scroll viewer cannot hold the conditional XAML switches: {exception}");
        }

        return null;
    }

    private bool TryPlaceInDeviceToolBar(SceneScrollViewer scrollViewer)
    {
        // SceneScrollViewer.DeviceToolBar is read by name, so that the extension builds against a Visual Studio
        // whose scroll viewer has no device toolbar; VsDesignerApiShapeTests pins the property. The toolbar is a
        // Border around a grid (devicetoolbar.xaml in Visual Studio 18) with eight columns: the device size, a
        // separator, the orientation, a separator, the effective resolution, a separator, and two empty ones.
        object? toolBar = _DeviceToolBarProperty?.GetValue(scrollViewer);

        // There is no toolbar of the shape that is known.
        if (toolBar is not Border { Child: Grid grid } || grid.ColumnDefinitions.Count == 0)
        {
            return false;
        }

        // Every column is occupied: the button gets a column of its own.
        int column = ConditionalXamlSwitchButton.FindFreeColumn(grid);
        if (column >= grid.ColumnDefinitions.Count)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            column = grid.ColumnDefinitions.Count - 1;
        }

        Grid.SetColumn(_button, column);
        Grid.SetRow(_button, 0);
        grid.Children.Add(_button);
        return true;
    }

    private bool TryPlaceInStrip(SceneScrollViewer scrollViewer)
    {
        // There is no content to put the strip above.
        if (scrollViewer.Content is not UIElement content)
        {
            return false;
        }

        var strip = new Border { Padding = new Thickness(3, 2, 3, 2), Child = _button };
        strip.SetResourceReference(Border.BackgroundProperty, EnvironmentColors.CommandBarGradientBeginBrushKey);
        strip.SetBinding(UIElement.VisibilityProperty, new Binding(nameof(UIElement.Visibility)) { Source = _button });
        DockPanel.SetDock(strip, Dock.Top);
        var panel = new DockPanel { LastChildFill = true };
        panel.Children.Add(strip);

        scrollViewer.Content = null;
        try
        {
            panel.Children.Add(content);
            scrollViewer.Content = panel;
        }
        catch
        {
            // Visual Studio's content goes back where it was; the designer must not lose it.
            panel.Children.Remove(content);
            scrollViewer.Content = content;
            throw;
        }

        return true;
    }

    /// <summary>
    /// Brings the button and, when it is open, the list up to date with the document, the project's overrides and
    /// the options of the extension.
    /// </summary>
    private void Refresh()
    {
        // The designer is gone.
        if (_isDisposed)
        {
            return;
        }

        SubscribeToBuffer(_getDocument()?.TextBuffer);
        SubscribeToProvider(DesignTimeTargetProvider.Get(_getProject()));
        ScanDocument();

        XamlConditionOverrides overrides = ReadOverrides();
        bool isShown = ConditionalXamlSwitchButton.IsShown(_isEnabled, _switches.Count, overrides.Count);
        _button.Visibility = isShown ? Visibility.Visible : Visibility.Collapsed;
        _buttonText.Text = ConditionalXamlSwitchRow.FormatButtonLabel(overrides.Count);
        AutomationProperties.SetItemStatus(_button, ConditionalXamlSwitchRow.FormatOverrideSummary(overrides.Count));

        if (!isShown)
        {
            // The button went away, and the list goes with it.
            ClosePopup();
        }
        else if (_popup.IsOpen)
        {
            // The list is open: it follows the document and the overrides.
            UpdateRows(overrides);
        }
    }

    private void ScanDocument()
    {
        // There is no document text.
        IMarkupTextBuffer? buffer = _buffer;
        if (buffer is null)
        {
            _switches = [];
            _scannedVersion = -1;
            return;
        }

        // Not every buffer counts versions, so the length takes part in telling one text from the next, and an
        // edit resets the version that was scanned.
        int version = buffer.Version;
        int length = buffer.Length;

        // The text is the one that was scanned.
        if (version == _scannedVersion && length == _scannedLength)
        {
            return;
        }

        string text = buffer.GetText(0, length);
        _switches = XamlConditionSwitches.Collect(XamlNamespaceScanner.Scan(text));
        _scannedVersion = version;
        _scannedLength = length;
    }

    private XamlConditionOverrides ReadOverrides()
    {
        try
        {
            return _provider?.DesignTimeOptions.ConditionOverrides ?? XamlConditionOverrides.Empty;
        }
        catch (Exception exception)
        {
            // The list shows the project as without overrides; the preview does not depend on what is shown here.
            WinUIDesignerLogger.LogTrace(_Category, $"Reading the conditional XAML overrides for the designer's user interface failed: {exception}");
            return XamlConditionOverrides.Empty;
        }
    }

    private void SubscribeToBuffer(IMarkupTextBuffer? buffer)
    {
        // Already listening to this buffer.
        if (ReferenceEquals(buffer, _buffer))
        {
            return;
        }

        // The buffer of an earlier document is left.
        if (_buffer is not null && _textChangedHandler is not null)
        {
            _buffer.TextChanged -= _textChangedHandler;
        }

        _buffer = buffer;
        _textChangedHandler = null;
        _scannedVersion = -1;

        // The buffer of the document now shown is listened to.
        if (buffer is not null)
        {
            // The buffer may outlive the designer; it must not keep the designer alive.
            var relay = new WeakRelay(this);
            _textChangedHandler = relay.OnTextChanged;
            buffer.TextChanged += _textChangedHandler;
        }
    }

    private void SubscribeToProvider(IDesignTimeTargetProvider? provider)
    {
        // Already listening to this surface process context.
        if (ReferenceEquals(provider, _provider))
        {
            return;
        }

        // The context of an earlier surface process is left.
        if (_provider is not null && _optionsChangedHandler is not null)
        {
            _provider.DesignTimeOptionsChanged -= _optionsChangedHandler;
        }

        _provider = provider;
        _optionsChangedHandler = null;

        // The context of the project now shown is listened to.
        if (provider is not null)
        {
            // The surface process context lives as long as the project; it must not keep the designer alive.
            var relay = new WeakRelay(this);
            _optionsChangedHandler = relay.OnDesignTimeOptionsChanged;
            provider.DesignTimeOptionsChanged += _optionsChangedHandler;
        }
    }

    private void OnTextChanged()
    {
        // The designer is gone.
        if (_isDisposed)
        {
            return;
        }

        // The text changed on another thread; the work belongs to the dispatcher of the designer.
        if (!_dispatcher.CheckAccess())
        {
            _ = _dispatcher.BeginInvoke(new Action(OnTextChanged));
            return;
        }

        // Scanned again once the user pauses typing.
        _scannedVersion = -1;
        _textChangedTimer.Stop();
        _textChangedTimer.Start();
    }

    private void OnDesignTimeOptionsChanged()
    {
        // The designer is gone.
        if (_isDisposed)
        {
            return;
        }

        // The options changed on another thread; the work belongs to the dispatcher of the designer.
        if (!_dispatcher.CheckAccess())
        {
            _ = _dispatcher.BeginInvoke(new Action(OnDesignTimeOptionsChanged));
            return;
        }

        Guard("following a change of the overrides", Refresh);
    }

    private void OpenPopup()
    {
        Refresh();

        // There is nothing to list: the button goes back to closed.
        if (_button.Visibility != Visibility.Visible)
        {
            _button.IsChecked = false;
            return;
        }

        XamlConditionOverrides overrides = ReadOverrides();
        UpdateRows(overrides);
        _popup.IsOpen = true;
        WinUIDesignerLogger.LogTrace(_Category, $"Conditional XAML switches: listing {_switches.Count} condition(s) of the document; the project forces {overrides.Count}.");
    }

    private void OnPopupOpened()
    {
        // A click on the button while the list is open only closes the list; it must not open it again.
        _button.IsHitTestVisible = false;
        _ = _dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => Guard("focusing the conditions", FocusFirstControl)));
    }

    private void OnPopupClosed()
    {
        _button.IsHitTestVisible = true;
        _button.IsChecked = false;
    }

    private void ClosePopup()
    {
        if (_popup.IsOpen || _button.IsChecked == true)
        {
            _popup.IsOpen = false;
            _button.IsChecked = false;
        }
    }

    private void FocusFirstControl()
    {
        // The list closed before the focus could move.
        if (!_popup.IsOpen)
        {
            return;
        }

        // The first row that can take the focus gets it.
        foreach (ConditionalXamlSwitchRowView row in _rowViews)
        {
            if (row.CheckBox.IsEnabled && row.CheckBox.Focus())
            {
                return;
            }
        }

        // No row can: Reset all gets it, and when it cannot either, the focus stays on the button.
        _resetAllButton.Focus();
    }

    private void OnButtonPreviewKeyDown(object sender, KeyEventArgs e)
    {
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        switch (ConditionalXamlSwitchButton.GetKeyAction(key, isOpen: _button.IsChecked == true))
        {
            case ConditionalXamlSwitchButton.KeyAction.Open:
                e.Handled = true;
                Guard("opening the conditions", () => _button.IsChecked = true);
                break;

            // No control of the list could take the focus, so Escape came to the button; Visual Studio's toolbar
            // would take the focus away and leave the list open.
            case ConditionalXamlSwitchButton.KeyAction.Close:
                e.Handled = true;
                Guard("closing the conditions", ClosePopup);
                break;
        }
    }

    private void OnPopupPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Only Escape is for the list; the other keys move through its controls.
        if (e.Key != Key.Escape)
        {
            return;
        }

        e.Handled = true;
        Guard("closing the conditions", () =>
        {
            ClosePopup();
            _button.Focus();
        });
    }

    private void UpdateRows(XamlConditionOverrides overrides)
    {
        // The evaluations belong to the surface process context, which also owns the overrides.
        Func<XamlConditionQuery, bool?>? evaluate = _provider is IXamlConditionEvaluationSource evaluations
            ? new Func<XamlConditionQuery, bool?>(evaluations.EvaluateIgnoringOverrides)
            : null;
        IReadOnlyList<ConditionalXamlSwitchRow> rows = ConditionalXamlSwitchRow.Create(_switches, overrides, evaluate);
        bool canSwitch = _provider is not null && WinUICreateSurfaceRequestInfo.IsTransportEnabled;

        // The same questions in the same order are updated in place, so that the focus stays where it is.
        bool isSameList = rows.Count == _rowViews.Count;
        for (int index = 0; isSameList && index < rows.Count; index++)
        {
            isSameList = string.Equals(rows[index].Key, _rowViews[index].Key, StringComparison.Ordinal);
        }

        if (!isSameList)
        {
            _rowsPanel.Children.Clear();
            _rowViews.Clear();
            foreach (ConditionalXamlSwitchRow row in rows)
            {
                var view = new ConditionalXamlSwitchRowView(
                    row.Key,
                    (switchedRow, holds) => Guard("switching a condition", () => SetOverride(switchedRow.Key, switchedRow.Title, holds)),
                    resetRow => Guard("resetting a condition", () => ResetOverride(resetRow.Key, resetRow.Title))
                );
                _rowViews.Add(view);
                _rowsPanel.Children.Add(view.Root);
            }
        }

        bool hasDocumentRows = false;
        for (int index = 0; index < rows.Count; index++)
        {
            _rowViews[index].Update(rows[index], canSwitch);
            hasDocumentRows |= rows[index].IsInDocument;
        }

        _emptyText.Visibility = hasDocumentRows ? Visibility.Collapsed : Visibility.Visible;
        _unavailableText.Visibility = canSwitch ? Visibility.Collapsed : Visibility.Visible;
        _summaryText.Text = ConditionalXamlSwitchRow.FormatOverrideSummary(overrides.Count);
        _resetAllButton.IsEnabled = canSwitch && !overrides.IsEmpty;
    }

    private void SetOverride(string key, string title, bool holds)
    {
        ChangeOverrides(
            overrides => overrides.With(key, holds),
            $"the user forces '{title}' to {(holds ? "hold" : "not hold")} (key '{key}')"
        );
    }

    private void ResetOverride(string key, string title)
    {
        ChangeOverrides(
            overrides => overrides.Without(key),
            $"'{title}' follows its evaluation again (key '{key}')"
        );
    }

    private void ResetAllOverrides()
    {
        ChangeOverrides(
            _ => XamlConditionOverrides.Empty,
            "every condition follows its evaluation again"
        );
    }

    private void ChangeOverrides(Func<XamlConditionOverrides, XamlConditionOverrides> change, string description)
    {
        // The options cannot reach the surface, so the choice would change nothing; the list shows the current state again.
        IDesignTimeTargetProvider? provider = _provider;
        if (provider is null || !WinUICreateSurfaceRequestInfo.IsTransportEnabled)
        {
            WinUIDesignerLogger.LogWarning(_Category, $"Conditional XAML switches: cannot apply the change because the design-time options cannot be sent to the surface; {description} is ignored.");
            Refresh();
            return;
        }

        DesignTimeOptions options = provider.DesignTimeOptions;
        XamlConditionOverrides overrides = change(options.ConditionOverrides);

        // A change that leaves the overrides as they are needs no reload.
        if (!overrides.Equals(options.ConditionOverrides))
        {
            WinUIDesignerLogger.LogInformation(_Category, $"Conditional XAML switches: {description}; the design surface reloads.");

            // Stores the choice, raises DesignTimeOptionsChanged and restarts the surface process.
            provider.SetDesignTimeOptions(options.WithConditionOverrides(overrides));
        }

        Refresh();
    }

    /// <summary>
    /// Runs a piece of the drop-down's work so that a failure never reaches Visual Studio. After a few failures
    /// the drop-down turns itself off for its designer.
    /// </summary>
    private void Guard(string action, Action work)
    {
        if (_isDisposed)
        {
            return;
        }

        try
        {
            work();
        }
        catch (Exception exception)
        {
            _failures++;
            bool turnOff = _failures >= _MaxFailures;
            WinUIDesignerLogger.LogWarning(
                _Category,
                $"Conditional XAML switches: {action} failed{(turnOff ? "; the switches are turned off for this designer" : string.Empty)}.",
                exception
            );

            // The drop-down gave up: it leaves the designer alone from now on.
            if (turnOff)
            {
                Dispose();
            }
        }
    }

    /// <summary>
    /// Forwards events of objects that live longer than the designer, without keeping the designer alive.
    /// </summary>
    private sealed class WeakRelay(ConditionalXamlSwitches owner)
    {
        private readonly WeakReference<ConditionalXamlSwitches> _owner = new(owner);

        public void OnTextChanged(object? sender, MarkupTextChangedEventArgs e)
        {
            if (_owner.TryGetTarget(out ConditionalXamlSwitches? target))
            {
                target.OnTextChanged();
            }
        }

        public void OnDesignTimeOptionsChanged(object? sender, EventArgs e)
        {
            if (_owner.TryGetTarget(out ConditionalXamlSwitches? target))
            {
                target.OnDesignTimeOptionsChanged();
            }
        }
    }
}
