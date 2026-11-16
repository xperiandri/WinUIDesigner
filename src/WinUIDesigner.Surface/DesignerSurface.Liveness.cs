// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;

namespace WinUIDesigner.Surface;

internal sealed partial class DesignerSurface
{
    /// <summary>
    /// Whether the framework still completes a render of this document: asks it to render one pixel of the island, and
    /// waits for it. A framework that has stopped drawing never answers. An element that cannot be rendered for another
    /// reason says nothing about the framework, so that it counts as drawing.
    /// </summary>
    public async Task<bool> CanDrawAsync(TimeSpan timeout)
    {
        try
        {
            var bitmap = new RenderTargetBitmap();
            var completed = new TaskCompletionSource();
            IAsyncAction render = bitmap.RenderAsync(_viewportRoot, 1, 1);
            render.Completed = (_, _) => completed.TrySetResult();

            if (await Task.WhenAny(completed.Task, Task.Delay(timeout)).ConfigureAwait(true) == completed.Task)
            {
                return true;
            }

            try
            {
                render.Cancel();
            }
            catch (Exception)
            {
                // The request is abandoned, and nothing waits for it.
            }

            return false;
        }
        catch (Exception)
        {
            return true;
        }
    }
}
