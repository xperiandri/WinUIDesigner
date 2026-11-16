// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Threading.Tasks;

namespace WinUIDesigner.Surface;

internal sealed partial class DesignerSurface
{
    // The dialog that this surface has put into its island, so that it is shown once.
    private ContentDialog? _shownDialog;

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
    /// The dialog lies over its island, which is the part of the viewport that is on screen, and no popup of it may be
    /// larger than that: the size of the device that the artboard gives to the root of a document would put the dialog
    /// far outside of it. So the dialog fills the island, as the dialog of an app fills its window, and the card in it
    /// (the part named <c>BackgroundElement</c> of the template, which the dialog does not stretch to its own size) is as
    /// large as the document asks for with <c>d:DesignWidth</c> and <c>d:DesignHeight</c>, where the island has room for
    /// it; without them the card is as large as its content.
    /// </summary>
    private void FitContentDialog()
    {
        if (_shownDialog is not { } dialog || _viewportRoot.XamlRoot is not { } xamlRoot)
        {
            return;
        }

        dialog.Width = xamlRoot.Size.Width;
        dialog.Height = xamlRoot.Size.Height;

        // The template exists once the dialog is shown; Loaded calls this again for the card.
        if (FindDialogCard(dialog) is not { } card)
        {
            return;
        }

        double designWidth = DT.GetDesignWidth(dialog);
        double designHeight = DT.GetDesignHeight(dialog);

        card.Width = double.IsNaN(designWidth) ? double.NaN : Math.Min(designWidth, xamlRoot.Size.Width);
        card.Height = double.IsNaN(designHeight) ? double.NaN : Math.Min(designHeight, xamlRoot.Size.Height);

        // The template limits the card (548 by 756 by default); a size that the document asks for is not limited by it.
        if (!double.IsNaN(card.Width) && card.MaxWidth < card.Width)
        {
            card.MaxWidth = card.Width;
        }

        if (!double.IsNaN(card.Height) && card.MaxHeight < card.Height)
        {
            card.MaxHeight = card.Height;
        }
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
