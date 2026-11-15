// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using Microsoft.VisualStudio.DesignTools.Markup.DocumentModel;
using Microsoft.VisualStudio.DesignTools.Markup.Metadata;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.InstanceBuilders;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.InstanceBuilders.Shared;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.InstanceBuilders.Uwp;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.Pipeline;
using WinUIDesigner.Markup;

namespace WinUIDesigner.Platform;

internal partial class WinUIInstanceBuilderPlatform
{
    private readonly DroppedNodeBuilder _droppedNodeBuilder = new();

    /// <summary>
    /// Chooses the builder that turns <paramref name="node"/> into construction actions. An element written
    /// under a dropped prefix, such as the branch of a conditional namespace that does not apply, gets a
    /// builder that creates nothing in the preview.
    /// </summary>
    /// <remarks>
    /// Properties are filtered by the instance manager, but an element in a collection or a dictionary is not
    /// a property of its parent: the builders create it through this method. The root is exempt, because a
    /// document needs its root.
    /// </remarks>
    public override IInstanceBuilder GetInstanceBuilder(IInstanceBuilderContext context, DocumentNode node)
    {
        if (node?.Parent is not null && MemberDispositions.GetNodeDisposition(node) == XamlPrefixDisposition.Drop)
        {
            return _droppedNodeBuilder;
        }

        return base.GetInstanceBuilder(context, node!);
    }

    /// <summary>
    /// Returns whether <paramref name="node"/> is an item of a collection that its parent addresses by index,
    /// such as a child of a panel, a column definition or a setter, rather than a property value or a dictionary entry.
    /// A node that cannot be judged counts as no such item, which leaves it built as nothing, as before.
    /// </summary>
    internal static bool IsIndexedCollectionItem(DocumentNode? node)
    {
        try
        {
            DocumentCompositeNode? parent = node?.Parent;
            if (node is null || parent is null || !node.IsChild)
            {
                return false;
            }

            // A resource dictionary keeps its entries by key; its builder adds them as dictionary entries.
            IKnownTypes knownTypes = node.PlatformMetadata.KnownTypes;
            return !knownTypes.DictionaryEntry.IsAssignableFrom(node.Type)
                && !knownTypes.IDictionary.IsAssignableFrom(parent.Type)
                && !knownTypes.ResourceDictionary.IsAssignableFrom(parent.Type);
        }
        catch (Exception ex)
        {
            WinUIDesignerLogger.LogTrace("Platform", $"Judging whether a dropped node is an item of an indexed collection failed: {ex}");
            return false;
        }
    }

    /// <summary>
    /// Builds nothing in the preview for a node, and nothing beneath it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The node has no object in the surface process. A property value, a dictionary entry or a key is marked
    /// invalid, which is how builders tell their callers that there is nothing to attach. Edits inside the node
    /// change nothing in the preview.
    /// </para>
    /// <para>
    /// An item of an indexed collection is different: the parent addresses its children by their index in the
    /// document model, which counts this node. The builder creates a stand-in that only the surface knows, and the
    /// parent adds it, removes it and looks it up like any other child, with the index of the node. The surface
    /// keeps the position as a hole of the live collection, so that the indices of the other children still
    /// reach the right objects, during the first build and after every edit.
    /// </para>
    /// </remarks>
    private sealed class DroppedNodeBuilder : UwpInstanceBuilder
    {
        public override bool CanInstantiate(IInstanceBuilderContext context, DocumentNode node)
        {
            // Not being able to create the node would be reported as an error; creating nothing is intended.
            return true;
        }

        public override void Instantiate(IInstanceBuilderContext context, LiveNodeProxy liveNode)
        {
            if (IsIndexedCollectionItem(liveNode.DocumentNode))
            {
                context.AddAction(new CreateInstanceAction(liveNode.LiveHandle, DroppedElementStandIn.TypeName));
                liveNode.MakeCreated();
                return;
            }

            liveNode.Invalidate();
        }

        public override void Initialize(IInstanceBuilderContext context, LiveNodeProxy liveNode, BuildOrder buildOrder)
        {
        }

        public override void ReferencePropertyOrChild(IInstanceBuilderContext context, DocumentNode childOrPropertyNode)
        {
            // There is no object to fetch a child from, so the path to an edited descendant ends here.
            context.GetLiveNodeProxy(childOrPropertyNode)?.Invalidate();
        }

        public override void UpdateProperty(IInstanceBuilderContext context, DocumentCompositeNode parentNode, DocumentNode oldNode, DocumentNode newNode, IProperty property)
        {
        }

        public override void UpdateEvent(IInstanceBuilderContext context, DocumentCompositeNode parentNode, DocumentNode oldNode, DocumentNode newNode, IEvent eventProperty)
        {
        }

        public override void UpdateChild(IInstanceBuilderContext context, DocumentCompositeNode parentNode, DocumentNode oldNode, DocumentNode newNode, int index)
        {
        }
    }
}
