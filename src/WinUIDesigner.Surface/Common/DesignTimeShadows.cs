// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;

namespace WinUIDesigner.Surface.Common;

/// <summary>
/// The design-time shadow properties that Visual Studio's designer sets on the elements of a document, and the
/// properties they stand for.
/// </summary>
/// <remarks>
/// <para>
/// Visual Studio keeps a design-time value of some properties next to the value of the markup, so that the designer can
/// show what the markup hides: <c>RuntimeVisibility</c> of <c>Visibility</c>, <c>RuntimeIsAppBarOpen</c> of
/// <c>IsOpen</c>, and so on. The construction actions of a document name such a property as
/// <c>RuntimeVisibility:XSurfUwp.DT</c> and use it in place of the real property when they build the instance
/// (<c>XamlDesignTimeProperties.RegisterShadow</c> in <c>Microsoft.VisualStudio.DesignTools.Markup.dll</c> lists them).
/// </para>
/// <para>
/// The surface declares the shadows of the size on <c>XSurfUwp.DT</c> (<c>RuntimeWidth</c>, <c>RuntimeHeight</c>), where
/// they combine with the design-time sizes. Every other shadow is applied to the property that it shadows: the
/// surface has no second store for it, so the value of the shadow replaces the value of the markup while the document
/// is built.
/// </para>
/// </remarks>
internal static class DesignTimeShadows
{
    private const string _DeclaringTypeName = "XSurfUwp.DT";

    // Only shadows of properties that exist on the WinUI types of the same name. Those of WPF-only members (the effect,
    // the media element, the popup placement) and of UWP-only members (the app bars of a page) have nothing to apply to.
    private static readonly Dictionary<string, string> _ShadowedProperties = new(StringComparer.Ordinal)
    {
        ["RuntimeVisibility"] = "Visibility",
        ["RuntimeOpacity"] = "Opacity",
        ["RuntimeIsHitTestVisible"] = "IsHitTestVisible",
        ["RuntimeDataContext"] = "DataContext",
        ["RuntimeUseLayoutRounding"] = "UseLayoutRounding",
        ["RuntimeIsZoomedInViewActive"] = "IsZoomedInViewActive",
        ["RuntimeIsAppBarOpen"] = "IsOpen",
        ["RuntimeSelectedIndex"] = "SelectedIndex",
        ["RuntimeIsPopupOpen"] = "IsOpen",
        ["RuntimePageTemplate"] = "Template",
        ["RuntimeWindowTemplate"] = "Template",
        ["RuntimeExpanderIsExpanded"] = "IsExpanded",
        ["RuntimeTreeViewItemIsExpanded"] = "IsExpanded",
        ["RuntimeRepeatBehavior"] = "RepeatBehavior",
        ["RuntimeCollectionViewSource"] = "Source",
        ["RuntimeShowsScrollingPlaceholders"] = "ShowsScrollingPlaceholders",
        ["RuntimeIsSplitViewPaneOpen"] = "IsPaneOpen",
        ["RuntimeIsDropDownOpen"] = "IsDropDownOpen",
        ["RuntimeSource"] = "Source",
        ["RuntimeFontFamily"] = "FontFamily",
        ["RuntimeFontStyle"] = "FontStyle",
        ["RuntimeFontStretch"] = "FontStretch",
        ["RuntimeFontWeight"] = "FontWeight",
    };

    /// <summary>
    /// Returns the property that a design-time shadow of Visual Studio stands for.
    /// </summary>
    /// <param name="fullPropertyName">The name of the property in a construction action: <c>RuntimeVisibility:XSurfUwp.DT</c>.</param>
    /// <param name="propertyName">The name of the shadowed property, for example <c>Visibility</c>.</param>
    /// <returns>
    /// <see langword="false"/> when the declaring type is not <c>XSurfUwp.DT</c> or the name is not one of the shadows that
    /// this class applies. The shadows of the size are not among them: <c>XSurfUwp.DT</c> declares those.
    /// </returns>
    public static bool TryGetShadowedProperty(string fullPropertyName, out string propertyName)
    {
        propertyName = string.Empty;

        // A property without a declaring type is not one of the properties of the designer's own type.
        int separator = fullPropertyName.IndexOf(':');
        if (separator <= 0)
        {
            return false;
        }

        // The declaring type may carry an assembly name after a comma.
        ReadOnlySpan<char> declaringType = fullPropertyName.AsSpan(separator + 1);
        int comma = declaringType.IndexOf(',');
        if (comma >= 0)
        {
            declaringType = declaringType[..comma];
        }

        if (declaringType.Trim().SequenceEqual(_DeclaringTypeName.AsSpan()) && _ShadowedProperties.TryGetValue(fullPropertyName[..separator], out string? shadowed))
        {
            propertyName = shadowed;
            return true;
        }

        return false;
    }
}
