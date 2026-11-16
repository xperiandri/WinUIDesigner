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
            FitContentDialog();
            _ = ShowDialogAsync(dialog);
        }
        catch (Exception exception)
        {
            // The document is shown without the dialog, as it was before.
            _shownDialog = null;
            WinUIDesignerLogger.LogTrace("Surface", "The preview of a ContentDialog could not be prepared.", exception);
        }
    }

    /// <summary>
    /// The dialog lays itself out over the area of its island, which is the part of the viewport that is on screen. The
    /// size of the device that the artboard gives to the root of a document would put the dialog far outside of it.
    /// </summary>
    private void FitContentDialog()
    {
        if (_shownDialog is { } dialog && _viewportRoot.XamlRoot is { } xamlRoot)
        {
            dialog.Width = xamlRoot.Size.Width;
            dialog.Height = xamlRoot.Size.Height;
        }
    }

    private async Task ShowDialogAsync(ContentDialog dialog)
    {
        try
        {
            _ = await dialog.ShowAsync();
        }
        catch (Exception exception)
        {
            WinUIDesignerLogger.LogTrace("Surface", "The ContentDialog of the preview could not be shown.", exception);
        }
        finally
        {
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
            WinUIDesignerLogger.LogTrace("Surface", "The ContentDialog of the preview could not be hidden.", exception);
        }
    }
}
