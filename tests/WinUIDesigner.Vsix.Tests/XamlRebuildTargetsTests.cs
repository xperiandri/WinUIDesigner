// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinUIDesigner.Platform;

/// <summary>
/// Covers which nodes of a document are built again when several places of it changed.
/// </summary>
[TestClass]
public sealed class XamlRebuildTargetsTests
{
    [TestMethod]
    public void NodeThatLiesInAnotherOneIsBuiltWithIt()
    {
        var root = new Node("root", null);
        var panel = new Node("panel", root);
        var border = new Node("border", panel);
        var text = new Node("text", border);

        IReadOnlyList<Node> targets = XamlRebuildTargets.Minimize([text, panel, border], node => node.Parent);

        CollectionAssert.AreEqual(new[] { panel }, targets.ToArray());
    }

    [TestMethod]
    public void NodesThatDoNotLieInEachOtherAreAllBuilt()
    {
        var root = new Node("root", null);
        var first = new Node("first", new Node("panel", root));
        var second = new Node("second", root);

        IReadOnlyList<Node> targets = XamlRebuildTargets.Minimize([first, second], node => node.Parent);

        CollectionAssert.AreEquivalent(new[] { first, second }, targets.ToArray());
    }

    [TestMethod]
    public void EachNodeIsBuiltOnce()
    {
        var root = new Node("root", null);
        var panel = new Node("panel", root);

        IReadOnlyList<Node> targets = XamlRebuildTargets.Minimize([panel, panel, panel], node => node.Parent);

        Assert.HasCount(1, targets);
    }

    [TestMethod]
    public void NothingIsBuiltForNoNodes() => Assert.IsEmpty(XamlRebuildTargets.Minimize([], (Node node) => node.Parent));

    [TestMethod]
    public void NodeIsItselfWhateverEqualsSaysOfIt()
    {
        // The document model compares some of its nodes by value; two elements that look alike are two elements.
        var root = new Node("root", null);
        var first = new AlwaysEqualNode("same", root);
        var second = new AlwaysEqualNode("same", root);

        IReadOnlyList<Node> targets = XamlRebuildTargets.Minimize<Node>([first, second], node => node.Parent);

        Assert.HasCount(2, targets);
    }

    [TestMethod]
    public void RootIsReturnedAsItIs()
    {
        // Whether the root can be built again is for the caller to say.
        var root = new Node("root", null);
        var child = new Node("child", root);

        IReadOnlyList<Node> targets = XamlRebuildTargets.Minimize([child, root], node => node.Parent);

        CollectionAssert.AreEqual(new[] { root }, targets.ToArray());
    }

    private class Node(string name, Node? parent)
    {
        public Node? Parent { get; } = parent;

        public override string ToString() => name;
    }

    private sealed class AlwaysEqualNode(string name, Node? parent) : Node(name, parent)
    {
        public override bool Equals(object? obj) => obj is AlwaysEqualNode;

        public override int GetHashCode() => 1;
    }
}
