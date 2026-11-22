// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;

namespace WinUIDesigner.Surface.Common;

/// <summary>The limits of the size of the card of a <c>ContentDialog</c>: its least and greatest width and height.</summary>
internal readonly record struct CardLimits(double MinWidth, double MaxWidth, double MinHeight, double MaxHeight);

/// <summary>
/// How the surface shows a <c>ContentDialog</c> that is the root of a document: the size of its card and of the area that
/// the card lies in. The input is plain data, so that tests reach it without a XAML island.
/// </summary>
internal static class ContentDialogPreviewPolicy
{
    /// <summary>
    /// The limits of the card for the design size that the document asks for (<see cref="double.NaN"/> in a direction that it
    /// says nothing about): the limits of the template, lowered or raised to the design size in a direction that has one,
    /// so that the card has that size, and the limits of the template in a direction that has none. They are computed from
    /// the limits of the template every time, so a design size that is removed or made smaller gives them back.
    /// </summary>
    public static CardLimits LimitsForDesignSize(CardLimits template, double designWidth, double designHeight)
    {
        return new CardLimits(
            double.IsNaN(designWidth) ? template.MinWidth : Math.Min(template.MinWidth, designWidth),
            double.IsNaN(designWidth) ? template.MaxWidth : Math.Max(template.MaxWidth, designWidth),
            double.IsNaN(designHeight) ? template.MinHeight : Math.Min(template.MinHeight, designHeight),
            double.IsNaN(designHeight) ? template.MaxHeight : Math.Max(template.MaxHeight, designHeight)
        );
    }

    /// <summary>
    /// The area that the card lies in, which is the size of the dialog: the artboard, so that the card is in its middle as a
    /// dialog is in the middle of the window of an app, or as large as the card with its margin
    /// (<paramref name="neededWidth"/> by <paramref name="neededHeight"/>) where that is larger.
    /// </summary>
    public static (double Width, double Height) DialogArea(double artboardWidth, double artboardHeight, double neededWidth, double neededHeight)
    {
        return (Math.Max(artboardWidth, neededWidth), Math.Max(artboardHeight, neededHeight));
    }
}
