// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.VisualStudio.DesignTools.SurfaceDesigner.Documents.SurfaceIsolation;
using Microsoft.VisualStudio.DesignTools.Utility.Text;
using WinUIDesigner.Markup;

namespace WinUIDesigner.Platform;

internal sealed partial class WinUIDesignerInstanceManager
{
    /// <summary>
    /// Whether a document with elements under an Uno platform prefix is cleaned a second time, so that those
    /// elements reach the surface in the prepared XAML and the namespace policies decide about them there.
    /// </summary>
    /// <remarks>
    /// Off by default: loading a document this way was confirmed in Visual Studio 18.13, but editing it
    /// afterwards and Visual Studio 17.14 have not been checked. While it is off, such a document is built
    /// from construction actions, which are generated from a document model that leaves out the members of
    /// an ignorable namespace nothing resolves. The environment variable
    /// <see cref="UnoPlatformPrefixes.ReprepareRawElementsVariable"/> turns it on when it is set to <c>1</c> or
    /// <c>true</c> before Visual Studio starts.
    /// </remarks>
    private static readonly bool _ReprepareRawPlatformElements = IsSwitchOn(UnoPlatformPrefixes.ReprepareRawElementsVariable);

    // The documents whose prepared XAML lost its platform elements. Only touched while a request is created.
    private readonly HashSet<int> _documentsBuiltFromActions = [];

    // The text of each document that was last checked for platform namespaces that are missing from mc:Ignorable.
    // Only touched while a request is created.
    private readonly ReportedTextVersions _reportedMissingIgnorable = new();

    /// <summary>
    /// Prepares the Uno platform the designer previews before a document is sent to the surface.
    /// </summary>
    private Task PreparePlatformPrefixesAsync(ISurfaceProcessDocument targetDocument, IReadOnlyList<ISurfaceProcessDocument> preparedDocuments)
    {
        // The designer previews the Windows flavor of the markup. The classification is the same for every target,
        // but another target would also have to keep every document on the prepared-XAML route (the second
        // cleaning): construction actions have no nodes for the members of an ignorable Uno namespace.
        _memberDispositions.TargetPlatform = UnoPlatformPrefixes.DefaultTargetPlatform;

        // The request carries exactly the target, App.xaml and the design-time resources, so only those are looked
        // at. The other documents that Visual Studio prepared, such as dictionaries merged through Source, are not
        // sent with it, and nothing in this package loads them: they keep their prepared XAML as it is.
        var seen = new HashSet<int>();
        PreparePlatformElements(targetDocument, seen);

        // App.xaml and the design-time resources are the business of the WinUI provider.
        if (MarkupProvider is WinUISurfaceProcessMarkupProvider provider)
        {
            // CreateRequestAsync sends App.xaml while it is prepared and the design-time resources only when they are
            // prepared. A document in the Loading state belongs to a request that is still waiting for the surface,
            // which may be reading its prepared file right now: the second cleaning would rewrite that file.
            if (provider.AppXaml is { ConstructionState: <= ConstructionState.Prepared } application)
            {
                PreparePlatformElements(application, seen);
            }

            // The design-time resources are sent when they are exactly prepared.
            if (provider.DesignTimeResources is { ConstructionState: ConstructionState.Prepared } resources)
            {
                PreparePlatformElements(resources, seen);
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Returns whether <paramref name="document"/> has to be built from construction actions because its
    /// prepared XAML no longer contains the elements that were written under an Uno platform prefix.
    /// </summary>
    private bool PlatformPrefixesRequireConstructionActions(ISurfaceProcessDocument? document)
    {
        return document is not null && _documentsBuiltFromActions.Contains(document.DocumentId);
    }

    /// <summary>
    /// Decides how a document with elements under an Uno platform prefix is built.
    /// A failure is logged and leaves the document to be built as it would be without platform prefixes.
    /// </summary>
    private void PreparePlatformElements(ISurfaceProcessDocument? document, HashSet<int> seen)
    {
        // A document that is missing, or that was looked at for this request already, needs nothing more.
        if (document is null || !seen.Add(document.DocumentId))
        {
            return;
        }

        int documentId = document.DocumentId;
        try
        {
            // The scan is cached per text version, and a document without Uno namespaces needs nothing further.
            XamlNamespaceScanResult namespaces = _memberDispositions.GetNamespaces(document.Document);
            string? path = null;
            if (namespaces.HasPlatformNamespaces)
            {
                path = document.Document?.DocumentContext?.DocumentPath;
                ReportPrefixesMissingFromIgnorable(document, namespaces, path);
            }

            switch (UnoPlatformPrefixes.ChooseBuild(namespaces, _ReprepareRawPlatformElements, () => TryReprepareRawPlatformElements(document)))
            {
                // The prepared XAML lost the elements: the document is built from construction actions.
                case PlatformElementsBuild.ConstructionActions:
                    _documentsBuiltFromActions.Add(documentId);
                    WinUIDesignerLogger.LogTrace("Platform", $"Document {documentId} ({path}) has elements under an Uno platform prefix; it is built from construction actions.");
                    break;

                // The prepared XAML was made again and keeps the elements.
                case PlatformElementsBuild.PreparedAgain:
                    _documentsBuiltFromActions.Remove(documentId);
                    WinUIDesignerLogger.LogTrace("Platform", $"Document {documentId} ({path}) was prepared again to keep its Uno platform elements.");
                    break;

                // The prepared XAML has everything the surface needs.
                default:
                    _documentsBuiltFromActions.Remove(documentId);
                    break;
            }
        }
        catch (Exception ex)
        {
            // The document is built as Visual Studio prepared it.
            WinUIDesignerLogger.LogWarning("Platform", $"Preparing the Uno platform prefixes of document {documentId} failed; it is built as prepared.", ex);
        }
    }

    private bool TryReprepareRawPlatformElements(ISurfaceProcessDocument document)
    {
        try
        {
            // Only the markup provider of this extension can prepare a document a second time.
            return MarkupProvider is WinUISurfaceProcessMarkupProvider provider && provider.ReprepareRawPlatformElements(document);
        }
        catch (Exception ex)
        {
            // The document is as Visual Studio prepared it, or about to be built from construction actions anyway.
            WinUIDesignerLogger.LogWarning("Platform", $"Preparing document {document.DocumentId} again failed; it is built from construction actions.", ex);
            return false;
        }
    }

    private void ReportPrefixesMissingFromIgnorable(ISurfaceProcessDocument document, XamlNamespaceScanResult namespaces, string? path)
    {
        // A document without a text buffer has no version to record, and nothing was scanned of it.
        IMarkupTextBuffer? buffer = document.Document?.TextBuffer;
        if (buffer is null)
        {
            return;
        }

        // Every request looks at the same text again; it is reported once for each text version of the document.
        IReadOnlyList<XamlNamespaceDeclaration> missing = UnoPlatformPrefixes.FindMissingIgnorableOncePerText(
            _reportedMissingIgnorable,
            document.DocumentId,
            buffer.Version,
            buffer.Length,
            namespaces
        );

        for (int index = 0; index < missing.Count; index++)
        {
            WinUIDesignerLogger.LogWarning("Platform", UnoPlatformPrefixes.DescribeMissingIgnorable(path, missing[index], _ReprepareRawPlatformElements));
        }
    }

    private static bool IsSwitchOn(string variable)
    {
        try
        {
            string? value = Environment.GetEnvironmentVariable(variable);
            return string.Equals(value, "1", StringComparison.Ordinal) || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            // Without access to the environment the switch stays off.
            return false;
        }
    }
}
