// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

#pragma warning disable IDE0130 // Namespace does not match folder structure
// Match the VS UWP designer contract: it names this fallback "XSurfUwp.Fallback.FallbackDataTemplateSelector, UwpSurface".
namespace XSurfUwp.Fallback;
#pragma warning restore IDE0130 // Namespace does not match folder structure

/// <summary>
/// Takes the place of a data template selector of the project that cannot be created, or that the designer must not
/// run. Selects no template, so the items show without one.
/// </summary>
public sealed partial class FallbackDataTemplateSelector : DataTemplateSelector
{
    /// <inheritdoc/>
    protected override DataTemplate? SelectTemplateCore(object item) => null;

    /// <inheritdoc/>
    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container) => null;
}
