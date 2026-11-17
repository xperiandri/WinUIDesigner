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

    // The theme of the dialog is made to take effect once for the theme that the preview has.
    private ElementTheme? _themeTakenByDialog;

    /// <summary>
    /// A <see cref="ContentDialog"/> draws nothing where it stands: it reports no size until it is shown, and showing
    /// it puts it into a popup. Show the dialog of a document that has one as its root in the island of the document,
    /// so that the preview shows the dialog. The popup lives in the island, so it goes with the island when the document
    /// is hidden or closed, and, unlike the document, it does not follow the pan and zoom of the artboard.
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
            _themeTakenByDialog = null;
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
    /// says nothing about. The dialog is the area that the card lies in: as large as the island, so that the card is in
    /// the middle of what is on screen, or as large as the card where the card is larger, so that nothing of the card is
    /// cut off by the area of the dialog (the island cuts off what lies outside of it, as it does for a page). The theme
    /// of the preview is the theme of the dialog. It changes nothing that is already as it should be, because a size
    /// that changes calls it again.
    /// </summary>
    private void FitContentDialog()
    {
        if (_disposed || _shownDialog is not { } dialog || _viewportRoot.XamlRoot is not { } xamlRoot)
        {
            return;
        }

        SyncContentDialogTheme();

        double islandWidth = xamlRoot.Size.Width;
        double islandHeight = xamlRoot.Size.Height;

        // The template exists once the dialog is shown; Loaded calls this again for the card.
        if (FindDialogCard(dialog) is not { } card)
        {
            SetSize(dialog, islandWidth, islandHeight);

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

        // The template limits the card (548 by 756 by default); a size that the document asks for is not limited by it.
        if (!double.IsNaN(designWidth) && card.MaxWidth < designWidth)
        {
            card.MaxWidth = designWidth;
        }

        if (!double.IsNaN(designHeight) && card.MaxHeight < designHeight)
        {
            card.MaxHeight = designHeight;
        }

        card.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Size needed = card.DesiredSize;

        SetSize(dialog, Math.Max(islandWidth, needed.Width), Math.Max(islandHeight, needed.Height));

        if (!_loggedDialogCard)
        {
            _loggedDialogCard = true;
            WinUIDesignerLogger.LogInformation(
                "Surface",
                $"The card of the ContentDialog ('{card.Name}') asks for {needed.Width}x{needed.Height} (design size {designWidth}x{designHeight}, NaN: not asked for) and lies in an area of {dialog.Width}x{dialog.Height} over an island of {islandWidth}x{islandHeight}.");
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

    /// <summary>
    /// The size of the dialog is the size of the area that the card lies in. Something else that sets it (the design size
    /// of the document, which Visual Studio applies to the root of the document, does) puts the card out of the middle.
    /// </summary>
    private void OnShownDialogSizeChanged(object sender, SizeChangedEventArgs e) => FitContentDialog();

    /// <summary>
    /// The dialog is shown in the popup layer of the island, not under the root of the document, so it does not take the
    /// theme of the preview from the root as the content of the document does, and what it took when its style was applied
    /// stays: the theme of the application (light unless the document says otherwise), as a white card around the dark
    /// content of a dark preview. The theme is set on the dialog, and the references to theme resources that its style
    /// and template made are resolved again for it (a theme that changes and changes back does that; setting the theme
    /// that it already has does not).
    /// </summary>
    private void SyncContentDialogTheme()
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

        if (dialog.IsLoaded && theme != ElementTheme.Default && _themeTakenByDialog != theme)
        {
            _themeTakenByDialog = theme;
            dialog.RequestedTheme = theme == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark;
            dialog.RequestedTheme = theme;

            // The values that the style gave to the dialog are resolved again by applying the style again: the content of
            // the dialog, which has no style of this kind, took the theme at once, and the card and the border did not.
            Brush? before = dialog.Background;
            WinUIDesignerLogger.LogTrace("Surface", $"The background of the ContentDialog after its theme was set to {theme}: {DescribeBrush(before)}.");
            ReapplyStyle(dialog);
            WinUIDesignerLogger.LogTrace("Surface", $"The background of the ContentDialog after its style was applied again: {DescribeBrush(dialog.Background)}.");
            if (ReferenceEquals(before, dialog.Background) || BrushColorEquals(before, dialog.Background))
            {
                RetargetDialogBrushes(dialog, theme);
                WinUIDesignerLogger.LogTrace("Surface", $"The background of the ContentDialog after its brushes were matched with the theme: {DescribeBrush(dialog.Background)}.");
            }

            // The template was made again with the style: its parts are looked for again.
            _ = dialog.DispatcherQueue.TryEnqueue(() => FitContentDialog());
        }
    }

    private static bool BrushColorEquals(Brush? left, Brush? right) => left is SolidColorBrush a && right is SolidColorBrush b && a.Color == b.Color && a.Opacity == b.Opacity;

    private static void ReapplyStyle(ContentDialog dialog)
    {
        try
        {
            Style? style = dialog.Style;
            if (style is null && Application.Current.Resources.TryGetValue(typeof(ContentDialog), out object? implicitStyle))
            {
                style = implicitStyle as Style;
            }

            if (style is not null)
            {
                dialog.Style = null;
                dialog.Style = style;
            }
        }
        catch (Exception exception)
        {
            WinUIDesignerLogger.LogTrace("Surface", "The style of the ContentDialog could not be applied again: " + exception.Message);
        }
    }

    /// <summary>
    /// A style that takes a brush from the resources of the application takes the one of the theme of the application,
    /// whatever the theme of the element is, and the theme of the application of this process is not the theme of the
    /// preview. For each brush that the dialog has, the key that it has in the theme dictionary that it comes from is
    /// found (it is the same object), and the brush of the theme of the preview under that key is taken instead.
    /// </summary>
    private static void RetargetDialogBrushes(ContentDialog dialog, ElementTheme theme)
    {
        string themeKey = theme == ElementTheme.Dark ? "Dark" : "Light";
        ResourceDictionary resources = Application.Current.Resources;

        if (RetargetedBrush(resources, dialog.Background, themeKey) is { } background)
        {
            dialog.Background = background;
        }

        if (RetargetedBrush(resources, dialog.BorderBrush, themeKey) is { } border)
        {
            dialog.BorderBrush = border;
        }

        if (RetargetedBrush(resources, dialog.Foreground, themeKey) is { } foreground)
        {
            dialog.Foreground = foreground;
        }
    }

    private static Brush? RetargetedBrush(ResourceDictionary resources, Brush? current, string themeKey)
    {
        if (current is null)
        {
            return null;
        }

        try
        {
            Brush? target = FindThemedBrush(resources, current, themeKey, 0);
            string how = "the same object";

            if (target is null && current is SolidColorBrush solid)
            {
                // Not the object of any dictionary (the style took a copy, or a brush that code made): the key is the one
                // that has this colour in the dictionaries of the other themes and another colour in the one of this theme.
                target = FindThemedBrushByColor(resources, solid, themeKey, 0, out string key);
                how = $"its colour, key '{key}'";
            }

            if (target is not null && !ReferenceEquals(target, current))
            {
                WinUIDesignerLogger.LogTrace("Surface", $"A brush of the ContentDialog, {DescribeBrush(current)}, is replaced by the one of the {themeKey} theme, {DescribeBrush(target)}, found by {how}.");
                return target;
            }

            WinUIDesignerLogger.LogTrace(
                "Surface",
                $"No brush of the {themeKey} theme was found for a brush of the ContentDialog, {DescribeBrush(current)}.{(current is SolidColorBrush solidCurrent ? " Candidates:" + DescribeBrushCandidates(resources, solidCurrent, 0) : string.Empty)}");
        }
        catch (Exception exception)
        {
            WinUIDesignerLogger.LogTrace("Surface", "The brushes of the ContentDialog could not be matched with a theme: " + exception.Message);
        }

        return null;
    }

    /// <summary>What the theme dictionaries have that looks like the brush: the keys of the brushes of the same colour, and the types that the entries have.</summary>
    private static string DescribeBrushCandidates(ResourceDictionary dictionary, SolidColorBrush current, int depth)
    {
        if (depth > 6)
        {
            return string.Empty;
        }

        System.Text.StringBuilder text = new();
        foreach (KeyValuePair<object, object> themed in dictionary.ThemeDictionaries)
        {
            if (themed.Value is not ResourceDictionary source)
            {
                continue;
            }

            ResourceDictionary? darkDictionary = FindThemeDictionary(dictionary, "Dark");
            List<string> same = [];
            foreach (KeyValuePair<object, object> entry in EnumerateEntries(source, 0))
            {
                if (entry.Value is SolidColorBrush brush && brush.Color == current.Color && same.Count < 6 && (entry.Key as string)?.StartsWith("Surface", StringComparison.Ordinal) != false)
                {
                    string counterpart = darkDictionary is null ? "no dark dictionary here" : darkDictionary.TryGetValue(entry.Key, out object? other) ? DescribeBrush(other as Brush) : "missing";
                    same.Add($"{entry.Key}{(brush.Opacity < 1 ? $"@{brush.Opacity}" : string.Empty)} -> {counterpart}");
                }
            }

            if (same.Count > 0)
            {
                text.Append($" [{themed.Key}, {source.Count} entries: {string.Join("; ", same)}]");
            }
        }

        foreach (ResourceDictionary merged in dictionary.MergedDictionaries)
        {
            text.Append(DescribeBrushCandidates(merged, current, depth + 1));
        }

        return text.ToString();
    }

    /// <summary>The shape of the resources, for the log when a brush is not found: how many entries and merged dictionaries each has, and which themes.</summary>
    private static string DescribeResources(ResourceDictionary dictionary, int depth)
    {
        if (depth > 3)
        {
            return "...";
        }

        string themes = string.Join(", ", dictionary.ThemeDictionaries.Select(pair => pair.Value is ResourceDictionary themed ? $"{pair.Key}({themed.Count} entries, {themed.MergedDictionaries.Count} merged{(themed.MergedDictionaries.Count > 0 ? ": " + string.Join(" ", themed.MergedDictionaries.Select(inner => $"[{inner.Count}/{inner.MergedDictionaries.Count}]")) : string.Empty)})" : $"{pair.Key}({pair.Value?.GetType().Name})"));
        string merged = string.Join(" ", dictionary.MergedDictionaries.Select(inner => DescribeResources(inner, depth + 1)));
        return $"{{{dictionary.GetType().Name} {dictionary.Count} entries{(themes.Length > 0 ? "; themes " + themes : string.Empty)}{(merged.Length > 0 ? "; merged " + merged : string.Empty)}}}";
    }

    /// <summary>The entries of a dictionary and of the dictionaries that it merges: a theme dictionary often only merges the files that have the brushes.</summary>
    private static IEnumerable<KeyValuePair<object, object>> EnumerateEntries(ResourceDictionary dictionary, int depth)
    {
        foreach (KeyValuePair<object, object> entry in dictionary)
        {
            yield return entry;
        }

        if (depth > 12)
        {
            yield break;
        }

        foreach (ResourceDictionary merged in dictionary.MergedDictionaries)
        {
            foreach (KeyValuePair<object, object> entry in EnumerateEntries(merged, depth + 1))
            {
                yield return entry;
            }
        }
    }

    /// <summary>
    /// The keys that a theme has in <c>ThemeDictionaries</c>: the dark theme of Windows is <c>Default</c> (there is
    /// no <c>Dark</c> in the dictionaries of WinUI, nor in those of Uno Material), and the palette of an app may
    /// say <c>Dark</c>.
    /// </summary>
    private static string[] ThemeKeys(string themeKey) => themeKey == "Dark" ? ["Dark", "Default"] : [themeKey];

    private static ResourceDictionary? FindThemeDictionary(ResourceDictionary dictionary, string themeKey)
    {
        foreach (string key in ThemeKeys(themeKey))
        {
            if (dictionary.ThemeDictionaries.TryGetValue(key, out object? themed) && themed is ResourceDictionary themeDictionary)
            {
                return themeDictionary;
            }
        }

        return null;
    }

    private static Brush? FindThemedBrushByColor(ResourceDictionary dictionary, SolidColorBrush current, string themeKey, int depth, out string key)
    {
        List<(string Key, SolidColorBrush Brush)> candidates = [];
        CollectBrushesByColor(dictionary, current, themeKey, depth, candidates);

        // The name that a surface has in the Material palette first, then any other name of a surface, then the shortest
        // name, which is the least specific: the brushes of WinUI, which many are white, come first in the resources.
        (string Key, SolidColorBrush Brush)? best = candidates
            .OrderBy(candidate => candidate.Key.Equals("SurfaceBrush", StringComparison.Ordinal) ? 0 : candidate.Key.StartsWith("Surface", StringComparison.Ordinal) ? 1 : 2)
            .ThenBy(candidate => candidate.Key.Length)
            .Select(candidate => ((string Key, SolidColorBrush Brush)?)candidate)
            .FirstOrDefault();

        key = best?.Key ?? string.Empty;
        return best?.Brush;
    }

    /// <summary>
    /// The brushes of the theme that has the key of a brush of the colour of <paramref name="current"/> in another theme
    /// and another colour in this one, in this dictionary and in the dictionaries that it merges.
    /// </summary>
    private static void CollectBrushesByColor(ResourceDictionary dictionary, SolidColorBrush current, string themeKey, int depth, List<(string Key, SolidColorBrush Brush)> candidates)
    {
        if (depth > 12)
        {
            return;
        }

        if (FindThemeDictionary(dictionary, themeKey) is { } targetDictionary)
        {
            foreach (KeyValuePair<object, object> themed in dictionary.ThemeDictionaries)
            {
                // The keys are objects: they are not tested for being strings, but written as the strings that they are.
                string? name = themed.Key?.ToString();
                if (name is null || ThemeKeys(themeKey).Contains(name) || themed.Value is not ResourceDictionary source)
                {
                    continue;
                }

                foreach (KeyValuePair<object, object> entry in EnumerateEntries(source, 0))
                {
                    string? entryKey = entry.Key?.ToString();
                    if (entryKey is not null
                        && entry.Value is SolidColorBrush brush
                        && brush.Color == current.Color
                        && brush.Opacity == current.Opacity
                        && targetDictionary.TryGetValue(entry.Key, out object? other)
                        && other is SolidColorBrush otherBrush
                        && (otherBrush.Color != brush.Color || otherBrush.Opacity != brush.Opacity))
                    {
                        candidates.Add((entryKey, otherBrush));
                    }
                }
            }
        }

        foreach (ResourceDictionary merged in dictionary.MergedDictionaries)
        {
            CollectBrushesByColor(merged, current, themeKey, depth + 1, candidates);
        }
    }

    private static Brush? FindThemedBrush(ResourceDictionary dictionary, Brush current, string themeKey, int depth)
    {
        if (depth > 12)
        {
            return null;
        }

        foreach (KeyValuePair<object, object> themed in dictionary.ThemeDictionaries)
        {
            if (themed.Value is not ResourceDictionary source)
            {
                continue;
            }

            foreach (KeyValuePair<object, object> entry in EnumerateEntries(source, 0))
            {
                if (ReferenceEquals(entry.Value, current)
                    && FindThemeDictionary(dictionary, themeKey) is { } targetDictionary
                    && targetDictionary.TryGetValue(entry.Key, out object? brush)
                    && brush is Brush found)
                {
                    return found;
                }
            }
        }

        foreach (ResourceDictionary merged in dictionary.MergedDictionaries)
        {
            if (FindThemedBrush(merged, current, themeKey, depth + 1) is { } result)
            {
                return result;
            }
        }

        return null;
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

    // A timer that nothing refers to is collected, and does not fire.
    private DispatcherQueueTimer? _dialogTreeTimer;

    /// <summary>Writes the layers of the dialog (names, backgrounds, borders, shadows) to the trace log a moment after it was shown.</summary>
    private void TraceContentDialogTreeLater()
    {
        if (!WinUIDesignerLogger.IsVerboseLoggingEnabled)
        {
            return;
        }

        DispatcherQueueTimer timer = _dialogTreeTimer ??= DispatcherQueue.GetForCurrentThread().CreateTimer();
        timer.Stop();
        timer.Interval = TimeSpan.FromMilliseconds(1500);
        timer.IsRepeating = false;
        timer.Tick -= OnDialogTreeTimerTick;
        timer.Tick += OnDialogTreeTimerTick;
        timer.Start();
    }

    private void OnDialogTreeTimerTick(DispatcherQueueTimer sender, object args)
    {
        sender.Stop();

        if (_shownDialog is not { } dialog)
        {
            return;
        }

        try
        {
            System.Text.StringBuilder text = new();
            text.Append("ContentDialog layers: ").AppendLine(DescribeContentDialog());
            text.Append("dialog Background=").Append(DescribeBrush(dialog.Background)).Append(" BorderBrush=").Append(DescribeBrush(dialog.BorderBrush)).Append(" BorderThickness=").Append(dialog.BorderThickness).Append(" CornerRadius=").Append(dialog.CornerRadius).Append(" Style=").AppendLine(dialog.Style is null ? "none" : dialog.Style.TargetType?.Name ?? "?");
            AppendVisualTree(dialog, 1, text);
            WinUIDesignerLogger.LogTrace("Surface", text.ToString());
        }
        catch (Exception exception)
        {
            WinUIDesignerLogger.LogTrace("Surface", "The layers of the ContentDialog could not be listed: " + exception.Message);
        }
    }

    private static string DescribeBrush(Brush? brush) => brush switch
    {
        null => "-",
        SolidColorBrush solid => solid.Color.ToString() + (solid.Opacity < 1 ? $"@{solid.Opacity}" : string.Empty),
        _ => brush.GetType().Name,
    };

    private static void AppendVisualTree(DependencyObject parent, int depth, System.Text.StringBuilder text)
    {
        if (depth > 9)
        {
            return;
        }

        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int index = 0; index < count; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, index);
            if (child is FrameworkElement element)
            {
                Brush? background = element switch { Border border => border.Background, Panel panel => panel.Background, Control control => control.Background, _ => null };
                Brush? borderBrush = element switch { Border border => border.BorderBrush, Control control => control.BorderBrush, _ => null };
                Thickness thickness = element switch { Border border => border.BorderThickness, Control control => control.BorderThickness, _ => default };
                CornerRadius radius = element switch { Border border => border.CornerRadius, Control control => control.CornerRadius, _ => default };

                bool interesting = background is not null || borderBrush is not null || element.Shadow is not null || !string.IsNullOrEmpty(element.Name);
                if (interesting)
                {
                    text.Append(' ', depth * 2).Append(element.GetType().Name);
                    if (!string.IsNullOrEmpty(element.Name))
                    {
                        text.Append(" '").Append(element.Name).Append('\'');
                    }

                    text.Append(" ").Append(element.ActualWidth).Append('x').Append(element.ActualHeight);
                    if (background is not null) { text.Append(" bg=").Append(DescribeBrush(background)); }
                    if (borderBrush is not null) { text.Append(" border=").Append(DescribeBrush(borderBrush)).Append(' ').Append(thickness); }
                    if (radius != default) { text.Append(" radius=").Append(radius); }
                    if (element.Shadow is { } shadow) { text.Append(" shadow=").Append(shadow.GetType().Name); }
                    if (element.Opacity < 1) { text.Append(" opacity=").Append(element.Opacity); }
                    if (element.Visibility != Visibility.Visible) { text.Append(" ").Append(element.Visibility); }
                    text.AppendLine();
                }
            }

            AppendVisualTree(child, depth + 1, text);
        }
    }

    /// <summary>What the framework says about the dialog, for the log when something is not drawn that should be.</summary>
    private string DescribeContentDialog()
    {
        if (_shownDialog is not { } dialog || _viewportRoot.XamlRoot is not { } root)
        {
            return "no dialog is shown";
        }

        FrameworkElement? card = FindDialogCard(dialog);
        string popups = string.Join(", ", VisualTreeHelper.GetOpenPopupsForXamlRoot(root).Select(popup => $"{popup.Child?.GetType().Name ?? "-"} {(popup.IsOpen ? "open" : "closed")}"));

        return $"dialog loaded={dialog.IsLoaded} opacity={dialog.Opacity} visibility={dialog.Visibility} actual={dialog.ActualWidth}x{dialog.ActualHeight} theme={dialog.ActualTheme}/{dialog.RequestedTheme}; "
            + (card is null ? "card not found; " : $"card actual={card.ActualWidth}x{card.ActualHeight} opacity={card.Opacity} visibility={card.Visibility} background={(card is Border border ? border.Background?.GetType().Name : "-")}; ")
            + $"open popups=[{popups}]; host visible={root.IsHostVisible}; island={root.Size.Width}x{root.Size.Height} scale={root.RasterizationScale}; root theme={_viewportRoot.ActualTheme}/{_viewportRoot.RequestedTheme}; application theme={Application.Current.RequestedTheme}; frozen={IsFrozen} rendering suspended={_renderingSuspended}";
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

    private void OnShownDialogLoaded(object sender, RoutedEventArgs e)
    {
        FitContentDialog();
        TraceContentDialogTreeLater();
    }

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
