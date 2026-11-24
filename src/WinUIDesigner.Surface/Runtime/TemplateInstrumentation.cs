// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Text.RegularExpressions;

namespace WinUIDesigner.Surface.Runtime;

/// <summary>
/// Removes the attributes with which Visual Studio marks the roots of the templates that it sends as XAML text.
/// </summary>
/// <remarks>
/// A template that a construction action carries is sent as text, and Visual Studio adds attached properties of
/// <c>DependencyObject</c> to its elements, <c>Microsoft_VisualStudio_DesignTools_TemplateDocumentId</c>,
/// <c>...TemplateNodeId</c> and <c>...TemplateHistoryVersion</c>, with which its UWP surface finds the template again in the
/// live tree. The XAML reader of WinUI does not know them (<c>The attachable property
/// 'Microsoft_VisualStudio_DesignTools_TemplateDocumentId' was not found in type 'DependencyObject'</c>) and rejects the whole
/// template, so every page with a template that is built from actions failed. A static preview needs only the template.
/// </remarks>
internal static partial class TemplateInstrumentation
{
    private const string _Marker = "Microsoft_VisualStudio_DesignTools_";

    /// <summary>
    /// Removes the instrumentation attributes from <paramref name="xaml"/>.
    /// </summary>
    /// <returns><paramref name="xaml"/> itself when it has none.</returns>
    public static string Remove(string xaml)
    {
        return xaml.Contains(_Marker, StringComparison.Ordinal) ? InstrumentationAttribute().Replace(xaml, string.Empty) : xaml;
    }

    // DependencyObject.Microsoft_VisualStudio_DesignTools_TemplateNodeId='5', also with a prefix and in double quotes, with
    // the white space that precedes it.
    [GeneratedRegex("""\s+(?:[\w.-]+:)?DependencyObject\.Microsoft_VisualStudio_DesignTools_\w+\s*=\s*(?:'[^']*'|"[^"]*")""")]
    private static partial Regex InstrumentationAttribute();
}
