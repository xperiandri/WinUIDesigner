// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
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
            dialog.Loaded += OnShownDialogLoaded;
            FitContentDialog();
            WinUIDesignerLogger.LogInformation("Surface", $"Showing the ContentDialog of a document over its island ({dialog.Width}x{dialog.Height}); design size {DT.GetDesignWidth(dialog)}x{DT.GetDesignHeight(dialog)} (NaN: not asked for).");
            _ = ShowDialogAsync(dialog);
        }
        catch (Exception exception)
        {
            // The document is shown without the dialog, as it was before.
            dialog.Loaded -= OnShownDialogLoaded;
            _shownDialog = null;
            WinUIDesignerLogger.LogWarning("Surface", "The preview of a ContentDialog could not be prepared.", exception);
        }
    }

    /// <summary>
    /// The card of the dialog (the part named <c>BackgroundElement</c> of the template; the dialog does not stretch it to
    /// its own size) is as large as the document asks for with <c>d:DesignWidth</c> and <c>d:DesignHeight</c>, each of
    /// them on its own and whatever the size of the designer area, and as large as its content asks for in a direction
    /// that the document says nothing about. The dialog is the area that the card lies in: as large as the island, so that
    /// the card is in the middle of what is on screen, or as large as the card where the card is larger, so that nothing of
    /// the card is cut off by the area of the dialog (the island cuts off what lies outside of it, as it does for a page).
    /// The theme of the preview is the theme of the dialog.
    /// </summary>
    private void FitContentDialog()
    {
        if (_shownDialog is not { } dialog || _viewportRoot.XamlRoot is not { } xamlRoot)
        {
            return;
        }

        SyncContentDialogTheme();

        double islandWidth = xamlRoot.Size.Width;
        double islandHeight = xamlRoot.Size.Height;

        // The template exists once the dialog is shown; Loaded calls this again for the card.
        if (FindDialogCard(dialog) is not { } card)
        {
            dialog.Width = islandWidth;
            dialog.Height = islandHeight;

            if (dialog.IsLoaded && !_loggedDialogCard)
            {
                _loggedDialogCard = true;
                WinUIDesignerLogger.LogInformation("Surface", "The template of the ContentDialog has no part named BackgroundElement; its card keeps the size of its content.");
            }

            return;
        }

        double designWidth = DT.GetDesignWidth(dialog);
        double designHeight = DT.GetDesignHeight(dialog);

        card.Width = designWidth;
        card.Height = designHeight;

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

        dialog.Width = Math.Max(islandWidth, needed.Width);
        dialog.Height = Math.Max(islandHeight, needed.Height);

        if (!_loggedDialogCard)
        {
            _loggedDialogCard = true;
            WinUIDesignerLogger.LogInformation(
                "Surface",
                $"The card of the ContentDialog asks for {needed.Width}x{needed.Height} (design size {designWidth}x{designHeight}, NaN: not asked for) and lies in an area of {dialog.Width}x{dialog.Height} over an island of {islandWidth}x{islandHeight}.");
        }

        if (WinUIDesignerLogger.IsVerboseLoggingEnabled)
        {
            WinUIDesignerLogger.LogTrace("Surface", "ContentDialog: " + DescribeContentDialog());
        }
    }

    /// <summary>
    /// The dialog is shown in the popup layer of the island, not under the root of the document, so it does not take the
    /// theme of the preview from the root as the content of the document does: it would be drawn in the theme of the
    /// application (light unless the document says otherwise), as a white card around the dark content of a dark preview.
    /// </summary>
    private void SyncContentDialogTheme()
    {
        if (_shownDialog is { } dialog && dialog.RequestedTheme != _viewportRoot.RequestedTheme)
        {
            dialog.RequestedTheme = _viewportRoot.RequestedTheme;
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
            + $"open popups=[{popups}]; host visible={root.IsHostVisible}; island={root.Size.Width}x{root.Size.Height} scale={root.RasterizationScale}; root theme={_viewportRoot.ActualTheme}/{_viewportRoot.RequestedTheme}";
    }

    private static FrameworkElement? FindDialogCard(DependencyObject parent)
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int index = 0; index < count; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, index);
            if (child is FrameworkElement { Name: "BackgroundElement" } card)
            {
                return card;
            }

            if (FindDialogCard(child) is { } found)
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
