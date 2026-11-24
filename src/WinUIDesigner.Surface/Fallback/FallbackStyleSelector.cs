// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

#pragma warning disable IDE0130 // Namespace does not match folder structure
// Match the VS UWP designer contract: it names this fallback "XSurfUwp.Fallback.FallbackStyleSelector, UwpSurface".
namespace XSurfUwp.Fallback;
#pragma warning restore IDE0130 // Namespace does not match folder structure

/// <summary>
/// Takes the place of a style selector of the project that cannot be created, or that the designer must not run.
/// Selects no style.
/// </summary>
public sealed partial class FallbackStyleSelector : StyleSelector
{
    /// <inheritdoc/>
    protected override Style? SelectStyleCore(object item, DependencyObject container) => null;
}
