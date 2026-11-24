// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using Microsoft.UI.Xaml.Data;

#pragma warning disable IDE0130 // Namespace does not match folder structure
// Match the VS UWP designer contract: it names the fallback for a converter "XSurfUwp.Fallback.FallbackValueConverter, UwpSurface".
namespace XSurfUwp.Fallback;
#pragma warning restore IDE0130 // Namespace does not match folder structure

/// <summary>
/// Takes the place of a value converter of the project that cannot be created, or that the designer must not run.
/// Passes every value through unchanged.
/// </summary>
public sealed partial class FallbackValueConverter : IValueConverter
{
    /// <inheritdoc/>
    public object? Convert(object? value, Type targetType, object? parameter, string? language) => value;

    /// <inheritdoc/>
    public object? ConvertBack(object? value, Type targetType, object? parameter, string? language) => value;
}
