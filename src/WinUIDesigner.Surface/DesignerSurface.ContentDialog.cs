// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Foundation;

namespace WinUIDesigner.Surface;

internal sealed partial class DesignerSurface
{
    // The dialog that this surface has put into its island, so that it is shown once.
    private ContentDialog? _shownDialog;

    // What happened to the card of the dialog is logged once.
    private bool _loggedDialogCard;

    // The card whose limits the surface sets, and the limits that its template gave it, which every design size starts from.
    private FrameworkElement? _limitedCard;
    private CardLimits _cardTemplateLimits;

    // Gives the dialog the brushes of the theme of the preview and remembers the ones that it wrote over those of the style.
    private readonly ContentDialogBrushRetargeter _dialogBrushes = new();

    // The pan and zoom of the artboard as Visual Studio sent them last. The dialog is not in the artboard (it is in the
    // popup layer of the island), so it is given them itself, see MoveDialogWithArtboard.
    private double _artboardOffsetX;
    private double _artboardOffsetY;
    private double _artboardScale = 1.0;

    /// <summary>
    /// A <see cref="ContentDialog"/> draws nothing where it stands: it reports no size until it is shown, and showing
    /// it puts it into a popup. Show the dialog of a document that has one as its root in the island of the document,
    /// so that the preview shows the dialog. The popup lives in the island, so it goes with the island when the document
    /// is hidden or closed. It is not under the canvas that Visual Studio pans and zooms, so it takes the pan and zoom
    /// itself (<see cref="MoveDialogWithArtboard"/>).
    /// </summary>
    private void PreviewContentDialog()
    {
        if (_content is not ContentDialog dialog
            || _xamlSource is null
            || _viewportRoot.XamlRoot is not { } xamlRoot)
        {
            return;
        }

        if (ReferenceEquals(_shownDialog, dialog))
        {
            FitContentDialog();
            return;
        }

        try
        {
            // A dialog cannot be shown while it has a parent.
            _transformRoot.Children.Remove(dialog);

            dialog.XamlRoot = xamlRoot;
            _shownDialog = dialog;
            dialog.Loaded += OnShownDialogLoaded;
            dialog.SizeChanged += OnShownDialogSizeChanged;
            FitContentDialog();
            WinUIDesignerLogger.LogInformation("Surface", $"Showing the ContentDialog of a document over its island ({dialog.Width}x{dialog.Height}); design size {DT.GetDesignWidth(dialog)}x{DT.GetDesignHeight(dialog)} (NaN: not asked for).");
            _ = ShowDialogAsync(dialog);
        }
        catch (Exception exception)
        {
            // The document is shown without the dialog, as it was before.
            dialog.Loaded -= OnShownDialogLoaded;
            dialog.SizeChanged -= OnShownDialogSizeChanged;
            _shownDialog = null;
            WinUIDesignerLogger.LogWarning("Surface", "The preview of a ContentDialog could not be prepared.", exception);
        }
    }

    /// <summary>
    /// The card of the dialog (see <see cref="FindDialogCard"/>; the dialog does not stretch it to its own size) is as
    /// large as the document asks for with <c>d:DesignWidth</c> and <c>d:DesignHeight</c>, each of them on its own and
    /// whatever the size of the designer area, and as large as its content asks for in a direction that the document
    /// says nothing about. The dialog is the area that the card lies in: the artboard (the size of the device), so that
    /// the card is in the middle of it, as a dialog is in the middle of the window of an app, or as large as the card
    /// with its margin where that is larger, so that nothing of the card is cut off by the area of the dialog. It takes
    /// the pan and zoom of the artboard (<see cref="MoveDialogWithArtboard"/>), so the card lies where Visual Studio
    /// thinks that it lies, and what lies outside of the designer area is reached by scrolling, as it is for a page.
    /// The theme of the preview is the theme of the dialog. It changes nothing that is already as it should be, because
    /// a size that changes calls it again.
    /// </summary>
    private void FitContentDialog()
    {
        if (_disposed || _shownDialog is not { } dialog || _viewportRoot.XamlRoot is null)
        {
            return;
        }

        SyncContentDialogTheme();
        ApplyArtboardTransformToDialog();

        double artboardWidth = _deviceWidth;
        double artboardHeight = _deviceHeight;

        // The template exists once the dialog is shown; Loaded calls this again for the card.
        if (FindDialogCard(dialog) is not { } card)
        {
            SetSize(dialog, artboardWidth, artboardHeight);

            if (dialog.IsLoaded && !_loggedDialogCard)
            {
                _loggedDialogCard = true;
                WinUIDesignerLogger.LogInformation("Surface", "The template of the ContentDialog has no part for its card; the card keeps the size of its content.");
            }

            return;
        }

        double designWidth = DT.GetDesignWidth(dialog);
        double designHeight = DT.GetDesignHeight(dialog);

        SetSize(card, designWidth, designHeight);

        // The template limits the card (320 to 548 wide and 184 to 756 high in WinUI); a size that the document asks for is not
        // limited by it, and the limits of the template come back when the document asks for another size or none.
        if (!ReferenceEquals(_limitedCard, card))
        {
            _limitedCard = card;
            _cardTemplateLimits = new CardLimits(card.MinWidth, card.MaxWidth, card.MinHeight, card.MaxHeight);
        }

        SetLimits(card, ContentDialogPreviewPolicy.LimitsForDesignSize(_cardTemplateLimits, designWidth, designHeight));

        card.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Size needed = card.DesiredSize;

        (double areaWidth, double areaHeight) = ContentDialogPreviewPolicy.DialogArea(artboardWidth, artboardHeight, needed.Width, needed.Height);
        SetSize(dialog, areaWidth, areaHeight);

        if (!_loggedDialogCard)
        {
            _loggedDialogCard = true;
            WinUIDesignerLogger.LogInformation(
                "Surface",
                $"The card of the ContentDialog ('{card.Name}') and its margin take {needed.Width}x{needed.Height} (design size {designWidth}x{designHeight}, NaN: not asked for); the dialog is an area of {dialog.Width}x{dialog.Height} over the artboard of {artboardWidth}x{artboardHeight}, which it follows when it is panned and zoomed.");
        }

        if (WinUIDesignerLogger.IsVerboseLoggingEnabled)
        {
            WinUIDesignerLogger.LogTrace("Surface", "ContentDialog: " + DescribeContentDialog());
        }
    }

    private static void SetSize(FrameworkElement element, double width, double height)
    {
        // Equal values (NaN equals NaN here) are not assigned again, so that nothing is laid out again for them.
        if (!element.Width.Equals(width))
        {
            element.Width = width;
        }

        if (!element.Height.Equals(height))
        {
            element.Height = height;
        }
    }

    private static void SetLimits(FrameworkElement card, CardLimits limits)
    {
        // As for the size, equal values are not assigned again.
        if (!card.MinWidth.Equals(limits.MinWidth))
        {
            card.MinWidth = limits.MinWidth;
        }

        if (!card.MaxWidth.Equals(limits.MaxWidth))
        {
            card.MaxWidth = limits.MaxWidth;
        }

        if (!card.MinHeight.Equals(limits.MinHeight))
        {
            card.MinHeight = limits.MinHeight;
        }

        if (!card.MaxHeight.Equals(limits.MaxHeight))
        {
            card.MaxHeight = limits.MaxHeight;
        }
    }

    /// <summary>
    /// Visual Studio pans and zooms the artboard by a transform on the canvas that the document is in
    /// (<see cref="SetPanZoomTransform"/>), and the selection frames, the guides and the hit tests that it computes are in
    /// the coordinates of that artboard. A dialog is in the popup layer of the island, which is not under that canvas, so
    /// without a transform of its own it stays where it is while the artboard moves: the picture looks frozen, and the
    /// frames of Visual Studio are somewhere else. The area of the dialog is the artboard, with its origin at the origin
    /// of the island, so the transform that the canvas has is the one that maps the dialog onto the screen too.
    /// </summary>
    private void MoveDialogWithArtboard(double offsetX, double offsetY, double scale)
    {
        _artboardOffsetX = offsetX;
        _artboardOffsetY = offsetY;
        _artboardScale = scale;
        ApplyArtboardTransformToDialog();
    }

    private void ApplyArtboardTransformToDialog()
    {
        if (_shownDialog is not { } dialog)
        {
            return;
        }

        // The transform belongs to the dialog (one transform cannot be the transform of two elements), and a pan changes
        // it in place.
        if (dialog.RenderTransform is not CompositeTransform transform)
        {
            transform = new CompositeTransform();
            dialog.RenderTransform = transform;
        }

        transform.ScaleX = _artboardScale;
        transform.ScaleY = _artboardScale;
        transform.TranslateX = _artboardOffsetX;
        transform.TranslateY = _artboardOffsetY;
    }

    /// <summary>
    /// The size of the dialog is the size of the area that the card lies in. Something else that sets it (the design size
    /// of the document, which Visual Studio applies to the root of the document, does) puts the card out of the middle.
    /// </summary>
    private void OnShownDialogSizeChanged(object sender, SizeChangedEventArgs e) => FitContentDialog();

    /// <summary>
    /// The dialog is shown in the popup layer of the island, not under the root of the document, so it does not take the
    /// theme of the preview from the root as the content of the document does: it has the theme of the application
    /// (light unless the document says otherwise), as a white card around the dark content of a dark preview. The theme
    /// is set on the dialog, as the documentation of the control advises. That does not reach the brushes that a style
    /// takes from the resources of the application (the style of Uno Material does), so once the template is there, whenever
    /// the theme of the preview or of the application changes, and after edits (<paramref name="afterEdits"/>), they are
    /// chosen for the theme of the preview (<see cref="RetargetDialogBrushes"/>).
    /// </summary>
    private void SyncContentDialogTheme(bool afterEdits = false)
    {
        if (_shownDialog is not { } dialog)
        {
            return;
        }

        ElementTheme theme = _viewportRoot.RequestedTheme;
        if (dialog.RequestedTheme != theme)
        {
            dialog.RequestedTheme = theme;
        }

        SyncSmokeLayerTheme(theme);

        if (dialog.IsLoaded)
        {
            RetargetDialogBrushes(dialog, theme, Application.Current.RequestedTheme, afterEdits);
        }
    }

    /// <summary>
    /// After a batch of edits, a change of theme, or the same root set again (<see cref="RefreshLayout"/>), the dialog that is
    /// shown is still the same object, but what it asks for can have changed: a design size that was removed, another style,
    /// a brush that the document set or removed. Its brushes are chosen again, the ones that the surface wrote taken back
    /// first, and it is fitted again.
    /// </summary>
    private void RefreshContentDialog()
    {
        if (_disposed || _shownDialog is null)
        {
            return;
        }

        SyncContentDialogTheme(afterEdits: true);
        FitContentDialog();
    }

    /// <summary>
    /// The layer of smoke that dims what lies behind a dialog is a popup of its own (a rectangle), filled by the framework
    /// with the colour that the theme of the application has for it: white at 60 per cent in the light theme (the system
    /// colour <c>SystemAltMediumColor</c>), black at 60 per cent in the dark one. The theme of the application of this
    /// process is the theme of the system, not the theme of the preview, so a dark preview got a light wash.
    /// </summary>
    private void SyncSmokeLayerTheme(ElementTheme theme)
    {
        if (_viewportRoot.XamlRoot is not { } root || theme == ElementTheme.Default)
        {
            return;
        }

        var smokeColor = theme == ElementTheme.Dark
            ? Windows.UI.Color.FromArgb(0x99, 0x00, 0x00, 0x00)
            : Windows.UI.Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF);

        foreach (Popup popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(root))
        {
            if (popup.Child is Microsoft.UI.Xaml.Shapes.Rectangle { Fill: SolidColorBrush current } smoke
                && current.Color != smokeColor)
            {
                WinUIDesignerLogger.LogTrace("Surface", $"The smoke layer of the ContentDialog, {DescribeBrush(current)}, is replaced by {smokeColor} for the {theme} theme of the preview.");
                smoke.Fill = new SolidColorBrush(smokeColor);
            }
        }
    }

    /// <summary>
    /// A style that takes a brush from the resources of the application takes the one of the theme of the application,
    /// whatever the theme of the element is, and the theme of the application of this process (the theme of the system) is
    /// not always the theme of the preview. The brushes that come from the theme of the application are replaced by the ones
    /// that <see cref="ContentDialogThemePolicy"/> chooses for the theme of the preview; the ones that the surface wrote for
    /// another theme are taken back first, and a brush that the document sets on the dialog stays.
    /// </summary>
    private void RetargetDialogBrushes(ContentDialog dialog, ElementTheme theme, ApplicationTheme application, bool afterEdits)
    {
        string? previewTheme = theme switch
        {
            ElementTheme.Dark => ContentDialogThemePolicy.Dark,
            ElementTheme.Light => ContentDialogThemePolicy.Light,
            _ => null,
        };
        string applicationTheme = application == ApplicationTheme.Dark ? ContentDialogThemePolicy.Dark : ContentDialogThemePolicy.Light;

        // Only the dictionaries of these two themes are read: the choice looks at no other.
        string[] themeKeys = previewTheme is null ? [] : [.. ContentDialogThemePolicy.ThemeKeys(applicationTheme), .. ContentDialogThemePolicy.ThemeKeys(previewTheme)];

        try
        {
            _dialogBrushes.Refresh(
                new DialogBrushProperties(dialog),
                previewTheme,
                applicationTheme,
                afterEdits,
                () => CollectThemeDictionaries(Application.Current.Resources, themeKeys, [], 0),
                value => value is Brush brush ? DescribeThemeBrush(brush) : null,
                LogDialogBrushChoice
            );
        }
        catch (Exception exception)
        {
            WinUIDesignerLogger.LogTrace("Surface", $"The brushes of the ContentDialog could not be matched with a theme: {exception.Message}");
        }
    }

    private static void LogDialogBrushChoice(string property, ThemeBrush current, BrushChoice choice)
    {
        string brush = DescribeBrush(current.Brush as Brush);
        WinUIDesignerLogger.LogTrace(
            "Surface",
            choice.Brush is { } target
                ? $"The {property} of the ContentDialog, {brush}, is replaced by {DescribeBrush(target.Brush as Brush)}: {choice.Reason}."
                : $"The {property} of the ContentDialog, {brush}, is kept: {choice.Reason}."
        );
    }

    /// <summary>
    /// The theme dictionaries of <paramref name="dictionary"/> and of the dictionaries that it merges, with the brushes of the
    /// themes that are named in <paramref name="themes"/>, as <see cref="ContentDialogThemePolicy"/> takes them.
    /// </summary>
    private static List<ThemeDictionarySet> CollectThemeDictionaries(ResourceDictionary dictionary, IReadOnlyCollection<string> themes, List<ThemeDictionarySet> sets, int depth)
    {
        if (depth > 12)
        {
            return sets;
        }

        if (dictionary.ThemeDictionaries.Count > 0)
        {
            Dictionary<string, IReadOnlyDictionary<string, ThemeBrush>> brushesByTheme = new(StringComparer.Ordinal);
            foreach (KeyValuePair<object, object> themed in dictionary.ThemeDictionaries)
            {
                // The keys are objects: they are not tested for being strings, but written as the strings that they are.
                if (themed.Key?.ToString() is { } name && themes.Contains(name) && themed.Value is ResourceDictionary themeDictionary)
                {
                    Dictionary<string, ThemeBrush> brushes = new(StringComparer.Ordinal);
                    CollectBrushes(themeDictionary, brushes, 0);
                    brushesByTheme[name] = brushes;
                }
            }

            sets.Add(new ThemeDictionarySet(brushesByTheme));
        }

        foreach (ResourceDictionary merged in dictionary.MergedDictionaries)
        {
            CollectThemeDictionaries(merged, themes, sets, depth + 1);
        }

        return sets;
    }

    /// <summary>
    /// The brushes of a dictionary by key, where a key is there more than once the one that a lookup finds: the entries of the
    /// dictionary itself, then those of the dictionaries that it merges, the last one first (a theme dictionary often only
    /// merges the files that have the brushes).
    /// </summary>
    private static void CollectBrushes(ResourceDictionary dictionary, Dictionary<string, ThemeBrush> brushes, int depth)
    {
        foreach (KeyValuePair<object, object> entry in dictionary)
        {
            if (entry.Key?.ToString() is { } key && entry.Value is Brush brush)
            {
                brushes.TryAdd(key, DescribeThemeBrush(brush));
            }
        }

        if (depth > 12)
        {
            return;
        }

        IList<ResourceDictionary> merged = dictionary.MergedDictionaries;
        for (int index = merged.Count - 1; index >= 0; index--)
        {
            CollectBrushes(merged[index], brushes, depth + 1);
        }
    }

    private static ThemeBrush DescribeThemeBrush(Brush brush)
    {
        if (brush is not SolidColorBrush solid)
        {
            return new ThemeBrush(brush, null, brush.Opacity);
        }

        Windows.UI.Color color = solid.Color;
        return new ThemeBrush(brush, ((uint)color.A << 24) | ((uint)color.R << 16) | ((uint)color.G << 8) | color.B, solid.Opacity);
    }

    /// <summary>The brush properties of a dialog, as <see cref="ContentDialogBrushRetargeter"/> takes them.</summary>
    private sealed class DialogBrushProperties(ContentDialog dialog) : IDialogBrushProperties
    {
        public bool TryGetLocalValue(string property, out object? value)
        {
            value = dialog.ReadLocalValue(Property(property));
            return !ReferenceEquals(value, DependencyProperty.UnsetValue);
        }

        public object? GetValue(string property) => dialog.GetValue(Property(property));

        public void SetValue(string property, object value) => dialog.SetValue(Property(property), value);

        public void ClearValue(string property) => dialog.ClearValue(Property(property));

        private static DependencyProperty Property(string property) => property switch
        {
            nameof(Control.Background) => Control.BackgroundProperty,
            nameof(Control.BorderBrush) => Control.BorderBrushProperty,
            nameof(Control.Foreground) => Control.ForegroundProperty,
            _ => throw new ArgumentOutOfRangeException(nameof(property), property, "Not a brush property of a ContentDialog."),
        };
    }

    /// <summary>
    /// The part of the template that is the card of the dialog: the one named <c>RealBackgroundElement</c> where the
    /// template has one (the Uno Material template draws its card, an elevated view, in that part, and hides the standard
    /// part), otherwise the standard <c>BackgroundElement</c>.
    /// </summary>
    private static FrameworkElement? FindDialogCard(DependencyObject dialog)
    {
        return FindNamedElement(dialog, "RealBackgroundElement") ?? FindNamedElement(dialog, "BackgroundElement");
    }

    private static string DescribeBrush(Brush? brush) => brush switch
    {
        null => "-",
        SolidColorBrush solid => solid.Color.ToString() + (solid.Opacity < 1 ? $"@{solid.Opacity}" : string.Empty),
        _ => brush.GetType().Name,
    };

    /// <summary>What the framework says about the dialog, for the log when something is not drawn that should be.</summary>
    private string DescribeContentDialog()
    {
        if (_shownDialog is not { } dialog || _viewportRoot.XamlRoot is not { } root)
        {
            return "no dialog is shown";
        }

        FrameworkElement? card = FindDialogCard(dialog);
        string popups = string.Join(", ", VisualTreeHelper.GetOpenPopupsForXamlRoot(root).Select(popup => $"{popup.Child?.GetType().Name ?? "-"} {(popup.IsOpen ? "open" : "closed")}"));

        return $"dialog loaded={dialog.IsLoaded} opacity={dialog.Opacity} visibility={dialog.Visibility} actual={dialog.ActualWidth}x{dialog.ActualHeight} theme={dialog.ActualTheme}/{dialog.RequestedTheme} background={DescribeBrush(dialog.Background)}; "
            + (card is null ? "card not found; " : $"card actual={card.ActualWidth}x{card.ActualHeight} opacity={card.Opacity} visibility={card.Visibility}; ")
            + $"open popups=[{popups}]; host visible={root.IsHostVisible}; island={root.Size.Width}x{root.Size.Height} scale={root.RasterizationScale}; artboard={_deviceWidth}x{_deviceHeight} pan=({_artboardOffsetX},{_artboardOffsetY}) zoom={_artboardScale}; root theme={_viewportRoot.ActualTheme}/{_viewportRoot.RequestedTheme}; application theme={Application.Current.RequestedTheme}; frozen={IsFrozen} rendering suspended={_renderingSuspended}";
    }

    private static FrameworkElement? FindNamedElement(DependencyObject parent, string name)
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int index = 0; index < count; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, index);
            if (child is FrameworkElement element && element.Name == name)
            {
                return element;
            }

            if (FindNamedElement(child, name) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private void OnShownDialogLoaded(object sender, RoutedEventArgs e) => FitContentDialog();

    private async Task ShowDialogAsync(ContentDialog dialog)
    {
        try
        {
            ContentDialogResult result = await dialog.ShowAsync();
            WinUIDesignerLogger.LogInformation("Surface", $"The ContentDialog of the preview was closed ({result}).");
        }
        catch (Exception exception)
        {
            WinUIDesignerLogger.LogWarning("Surface", "The ContentDialog of the preview could not be shown.", exception);
        }
        finally
        {
            dialog.Loaded -= OnShownDialogLoaded;
            dialog.SizeChanged -= OnShownDialogSizeChanged;

            if (ReferenceEquals(_shownDialog, dialog))
            {
                _shownDialog = null;
            }
        }
    }

    /// <summary>
    /// Closes the dialog that the surface showed, when its root is replaced. It is not called when the surface is
    /// disposed: the island takes its popups with it, and closing a dialog whose island is going away ends the process.
    /// </summary>
    private void HideContentDialogPreview()
    {
        ContentDialog? dialog = _shownDialog;
        _shownDialog = null;

        // The brushes that the surface wrote belong to the dialog that goes.
        _dialogBrushes.Forget();

        if (dialog is not null)
        {
            dialog.Loaded -= OnShownDialogLoaded;
            dialog.SizeChanged -= OnShownDialogSizeChanged;
        }

        if (dialog is null || _viewportRoot.XamlRoot is not { IsHostVisible: true })
        {
            return;
        }

        try
        {
            dialog.Hide();
        }
        catch (Exception exception)
        {
            WinUIDesignerLogger.LogWarning("Surface", "The ContentDialog of the preview could not be hidden.", exception);
        }
    }
}
