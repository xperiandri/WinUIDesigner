// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.DesignTools.Markup;
using Microsoft.VisualStudio.DesignTools.Markup.DocumentModel;

namespace WinUIDesigner.Platform;

/// <summary>
/// Notices the edits that change which members of a document the namespace policies drop without changing the
/// document model, and has the preview built again for the elements they concern.
/// </summary>
/// <remarks>
/// <para>
/// Visual Studio's incremental update of the document model compares the types and the values of what the text
/// describes. The prefix of an element or of an attribute, and the condition of an <c>xmlns</c> declaration, are
/// part of neither: changing only one of them updates the source references of the nodes and raises no change,
/// so nothing is built. The preview then shows the wrong branch, and the edits that follow are discarded, or land
/// on a neighbour.
/// </para>
/// <para>
/// The watcher compares the verdicts of the document after each successful parse of an edit with those of the
/// last look. For each place that went from built to dropped or the other way it raises the change that Visual
/// Studio itself uses to build a node again, for the node that decides what is built there: the element that
/// holds a changed element, or the element that has a changed property. The change goes the way of every other
/// change of the document, and builds the node with the verdicts of now. A place directly in the root, and the
/// root itself, cannot be built again this way: Visual Studio reloads a designer for a change of the root.
/// </para>
/// </remarks>
internal sealed class XamlVerdictWatcher(XamlMemberDispositionService dispositions, Func<bool> isEnabled)
{
    private readonly ConditionalWeakTable<IDocumentRoot, WatchedDocument> _watched = new();

    /// <summary>
    /// Starts to watch <paramref name="document"/>, or takes it as it is now when it is watched already: the
    /// preview is built from the document, so what it shows is in step with it.
    /// </summary>
    /// <remarks>Call it on the thread that edits the document, when the document is loaded.</remarks>
    public void Watch(IDocumentRoot? document)
    {
        if (document is not IMarkupDocument markup)
        {
            return;
        }

        try
        {
            WatchedDocument watched = _watched.GetValue(document, _ => new WatchedDocument());
            lock (watched)
            {
                if (!watched.IsSubscribed)
                {
                    markup.ParseCompleted += OnParseCompleted;
                    markup.Disposing += OnDisposing;
                    watched.IsSubscribed = true;
                }

                // Whatever the preview showed is replaced by what is built now.
                watched.Ledger.Reset();
                watched.Ledger.Update(() => dispositions.GetDroppedPlaces(document), dispositions.HasPolicyNamespaces(document));
            }
        }
        catch (Exception ex)
        {
            WinUIDesignerLogger.LogTrace("Platform", $"Watching a document for edits of conditional prefixes failed: {ex}");
        }
    }

    // This runs inside the parse of an edit, which must not fail because of what is done here.
    private void OnParseCompleted(object? sender, EventArgs e)
    {
        try
        {
            if (sender is not IMarkupDocument document || !_watched.TryGetValue(document, out WatchedDocument? watched))
            {
                return;
            }

            // The pipeline of a designer that is gone builds nothing; the document does not need to be watched for it.
            if (!isEnabled())
            {
                Stop(document, watched);
                return;
            }

            IReadOnlyList<XamlVerdictPlace> changed;
            lock (watched)
            {
                changed = watched.Ledger.Update(() => dispositions.GetDroppedPlaces(document), dispositions.HasPolicyNamespaces(document));
            }

            if (changed.Count > 0)
            {
                RequestRebuild(document, changed);
            }
        }
        catch (Exception ex)
        {
            WinUIDesignerLogger.LogTrace("Platform", $"Looking for an edit of a conditional prefix failed: {ex}");
        }
    }

    private void OnDisposing(object? sender, EventArgs e)
    {
        if (sender is IMarkupDocument document && _watched.TryGetValue(document, out WatchedDocument? watched))
        {
            Stop(document, watched);
        }
    }

    private void Stop(IMarkupDocument document, WatchedDocument watched)
    {
        lock (watched)
        {
            document.ParseCompleted -= OnParseCompleted;
            document.Disposing -= OnDisposing;
            watched.IsSubscribed = false;
            watched.Ledger.Reset();
        }
    }

    private static void RequestRebuild(IMarkupDocument document, IReadOnlyList<XamlVerdictPlace> changed)
    {
        // The node that decides what is built: the element that holds a changed element, or that has a changed property.
        IEnumerable<DocumentCompositeNode> deciding = changed
            .Select(place => place.Property is null ? place.Node.Parent : place.Node)
            .OfType<DocumentCompositeNode>()
            .Where(node => node.IsInDocument);
        IReadOnlyList<DocumentCompositeNode> targets = XamlRebuildTargets.Minimize(deciding, node => node.Parent);

        int rebuilt = 0;
        bool needsReload = false;
        foreach (DocumentCompositeNode target in targets)
        {
            // A change of the root is a change of the designer's view, which Visual Studio reloads.
            if (target.Parent is null)
            {
                needsReload = true;
                continue;
            }

            document.OnNodeChange(target, MarkupUtility.CreateRebuild(target));
            rebuilt++;
        }

        if (rebuilt > 0)
        {
            WinUIDesignerLogger.LogTrace("Platform", $"Conditional XAML: an edit changed which of {changed.Count} member(s) apply; the preview is built again for {rebuilt} element(s).");
        }

        if (needsReload)
        {
            WinUIDesignerLogger.LogWarning(
                "Platform",
                "Conditional XAML: an edit changed which members of the root element, or directly inside it, apply. "
                + "The preview shows them as before until the designer is reloaded.");
        }
    }

    /// <summary>
    /// What is known of a document that is watched.
    /// </summary>
    private sealed class WatchedDocument
    {
        public XamlVerdictLedger<XamlVerdictPlace> Ledger { get; } = new();

        public bool IsSubscribed { get; set; }
    }
}
