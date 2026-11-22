// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Xml.Linq;

namespace WinUIDesigner.Surface.Common;

/// <summary>
/// Maps the stand-in types of Visual Studio's UWP surface to the types that this surface creates in their place.
/// </summary>
/// <remarks>
/// The UWP designer that this designer builds on replaces some types with types of its own surface process
/// (<c>UwpStandInInfo</c> and <c>UwpDesignerInstanceBuilderPlatform</c> in Visual Studio). Its instance builders name
/// them in the assembly <c>UwpSurface</c>, which this process does not have, and its cleaner writes them into
/// prepared XAML in the namespace <c>using:XSurfUwp</c>. The fallback controls are not mapped here:
/// <c>XamlRuntimeUtilities.ResolveType</c> and the preparation of the XAML turn them into a <c>ContentControl</c>.
/// </remarks>
internal static class UwpStandIns
{
    private const string _StandInNamespace = "using:XSurfUwp";
    private const string _PresentationNamespace = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    /// <summary>
    /// Returns the full name of the type that this surface creates for a stand-in type of Visual Studio's UWP
    /// surface, or <see langword="null"/> when <paramref name="serializedTypeName"/> names no such stand-in.
    /// </summary>
    /// <param name="serializedTypeName">The type name as Visual Studio sends it, with or without its assembly.</param>
    public static string? GetRuntimeTypeName(string? serializedTypeName)
    {
        if (string.IsNullOrWhiteSpace(serializedTypeName))
        {
            return null;
        }

        ReadOnlySpan<char> fullName = serializedTypeName.AsSpan();
        int assemblySeparator = fullName.IndexOf(',');
        fullName = (assemblySeparator < 0 ? fullName : fullName[..assemblySeparator]).Trim();
        return fullName switch
        {
            // The trigger of adaptive visual states: the UWP surface evaluates its own copy against the size of the
            // design device, this surface creates the trigger of WinUI.
            "XSurfUwp.AdaptiveTrigger" => "Microsoft.UI.Xaml.AdaptiveTrigger",
            // d:DesignInstance, and the fallbacks for the value converters and the selectors of the project: this
            // surface defines them under the names of the UWP surface.
            "XSurfUwp.DesignInstance"
                or "XSurfUwp.Fallback.FallbackValueConverter"
                or "XSurfUwp.Fallback.FallbackDataTemplateSelector"
                or "XSurfUwp.Fallback.FallbackGroupStyleSelector"
                or "XSurfUwp.Fallback.FallbackStyleSelector" => fullName.ToString(),
            // Any other type, including the fallback controls.
            _ => null,
        };
    }

    /// <summary>
    /// Returns the name under which the WinUI XAML reader knows an element that Visual Studio's cleaner wrote into
    /// prepared XAML as a stand-in of its UWP surface, or <see langword="null"/> when the element is no such stand-in.
    /// </summary>
    /// <param name="elementName">The name of an element of prepared XAML.</param>
    public static XName? GetRuntimeElementName(XName elementName)
    {
        // The cleaner writes AdaptiveTrigger as {using:XSurfUwp}AdaptiveTrigger; the trigger of WinUI has the same name.
        return elementName.NamespaceName == _StandInNamespace && elementName.LocalName == "AdaptiveTrigger"
            ? XName.Get(elementName.LocalName, _PresentationNamespace)
            : null;
    }

    /// <summary>
    /// Renames, in place, the elements of prepared XAML that Visual Studio's cleaner wrote as stand-ins of its UWP
    /// surface to the names under which the WinUI XAML reader knows them (<see cref="GetRuntimeElementName"/>).
    /// </summary>
    /// <param name="root">The root of the prepared XAML, which is renamed too when it is a stand-in.</param>
    public static void RenameStandIns(XElement root)
    {
        foreach (XElement element in root.DescendantsAndSelf())
        {
            if (GetRuntimeElementName(element.Name) is { } runtimeName)
            {
                element.Name = runtimeName;
            }
        }
    }
}
