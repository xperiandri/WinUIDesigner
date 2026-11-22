// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace WinUIDesigner.Platform;

/// <summary>
/// Chooses the nodes of a document that the preview has to build again.
/// </summary>
internal static class XamlRebuildTargets
{
    /// <summary>
    /// Returns the smallest set of nodes that covers <paramref name="nodes"/>: each node once, and none that lies
    /// inside another, because building a node builds everything inside it.
    /// </summary>
    /// <param name="nodes">The nodes that have to be built again.</param>
    /// <param name="getParent">Returns the node a node lies in, or <see langword="null"/> for the root of the document.</param>
    /// <typeparam name="TNode">A node of a document.</typeparam>
    public static IReadOnlyList<TNode> Minimize<TNode>(IEnumerable<TNode> nodes, Func<TNode, TNode?> getParent)
        where TNode : class
    {
        // A node is itself, whatever Equals says about the nodes of a document.
        var wanted = new HashSet<TNode>(nodes, IdentityComparer<TNode>.Instance);
        var targets = new List<TNode>();
        foreach (TNode node in wanted)
        {
            // A node under another one that is built again is built with it.
            if (!HasAncestorIn(node, wanted, getParent))
            {
                targets.Add(node);
            }
        }

        return targets;
    }

    private static bool HasAncestorIn<TNode>(TNode node, HashSet<TNode> nodes, Func<TNode, TNode?> getParent)
        where TNode : class
    {
        for (TNode? ancestor = getParent(node); ancestor is not null; ancestor = getParent(ancestor))
        {
            if (nodes.Contains(ancestor))
            {
                return true;
            }
        }

        return false;
    }

    private sealed class IdentityComparer<T> : IEqualityComparer<T>
        where T : class
    {
        public static IdentityComparer<T> Instance { get; } = new();

        public bool Equals(T? x, T? y) => ReferenceEquals(x, y);

        public int GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
