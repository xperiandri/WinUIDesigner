// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;
using System.Xml.Linq;
using WinUIDesigner.Surface.Markup;

namespace WinUIDesigner.Surface.Services;

/// <summary>
/// Applies the XAML namespace policies (conditional namespaces and Uno platform prefixes)
/// to XAML before the WinUI XAML reader loads it.
/// </summary>
internal sealed partial class SurfaceService
{
    /// <summary>
    /// Parses prepared XAML once, with line information, and applies the namespace policies to the result.
    /// </summary>
    /// <remarks>
    /// A policy failure never fails the document: it is logged and the XAML is used as prepared.
    /// </remarks>
    private XDocument LoadPreparedXaml(string xaml, int documentId)
    {
        return LoadPreparedXaml(xaml, documentId, out _);
    }

    /// <summary>
    /// Parses prepared XAML once, with line information, and applies the namespace policies to the result.
    /// </summary>
    /// <param name="xaml">The prepared XAML.</param>
    /// <param name="documentId">The document, for logging.</param>
    /// <param name="droppedChildren">
    /// The elements the policies removed from a collection that Visual Studio's document model still counts;
    /// empty when nothing was removed or the policies failed.
    /// </param>
    /// <remarks>
    /// A policy failure never fails the document: it is logged and the XAML is used as prepared.
    /// </remarks>
    private XDocument LoadPreparedXaml(string xaml, int documentId, out IReadOnlyList<XamlDroppedChild> droppedChildren)
    {
        const LoadOptions options = LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo;

        droppedChildren = Array.Empty<XamlDroppedChild>();
        XDocument document = XDocument.Parse(xaml, options);
        try
        {
            XamlNamespaceRewriteResult result = XamlNamespaceRewriter.Rewrite(
                document,
                CreateNamespaceRewriteOptions(xaml, $"document {documentId}")
            );
            if (result.Changed)
            {
                WinUIDesignerLogger.LogTrace("Surface", $"Namespace policies rewrote document {documentId}: dropped {result.DroppedElements} element(s) and {result.DroppedAttributes} attribute(s), normalized {result.NormalizedMembers} member(s).");
            }

            droppedChildren = result.DroppedChildren;
            return document;
        }
        catch (Exception ex)
        {
            // The rewriter works in place, so a failed run may have left the document half rewritten.
            WinUIDesignerLogger.LogWarning("Surface", $"Namespace policies failed for document {documentId}; continuing with the XAML as prepared.", ex);
            return XDocument.Parse(xaml, options);
        }
    }

    private XamlNamespaceRewriteOptions CreateNamespaceRewriteOptions(string xaml, string description)
    {
        var options = new XamlNamespaceRewriteOptions
        {
            SourceXaml = xaml,
            Report = message => WinUIDesignerLogger.LogTrace("Surface", $"Namespace policies ({description}): {message}"),
            ReportWarning = message => WinUIDesignerLogger.LogWarning("Surface", $"Namespace policies ({description}): {message}"),
        };

        // Each policy configures itself in its own file. The platform decides first, then the condition.
        ConfigurePlatformPrefixPolicy(options);
        ConfigureConditionalNamespacePolicy(options);

        return options;
    }
}
