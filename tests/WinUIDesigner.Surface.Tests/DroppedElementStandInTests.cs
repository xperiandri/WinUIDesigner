// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using Microsoft.VisualStudio.DesignTools.RuntimeHost.InstanceBuilders.Shared;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinUIDesigner.Markup;

/// <summary>
/// Pins the name of the stand-in for a dropped item of a collection, which Visual Studio's instance builder writes
/// into a construction action and the surface recognizes. Both processes compile this name, so the tests run on both
/// runtimes.
/// </summary>
[TestClass]
public sealed class DroppedElementStandInTests
{
    [TestMethod]
    [DataRow("XSurfUwp.DroppedElement")]
    [DataRow("XSurfUwp.DroppedElement, WinUISurface")]
    [DataRow(" XSurfUwp.DroppedElement , WinUISurface, Version=1.0.0.0")]
    public void TheNameIsRecognizedWithOrWithoutAnAssembly(string typeName)
    {
        Assert.IsTrue(DroppedElementStandIn.IsTypeName(typeName));
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("XSurfUwp.Fallback.FallbackControl, UwpSurface")]
    [DataRow("XSurfUwp.DroppedElementX")]
    [DataRow("xsurfuwp.droppedelement")]
    [DataRow("Microsoft.UI.Xaml.Controls.TextBlock, Microsoft.WinUI")]
    public void OtherNamesAreNotTheStandIn(string? typeName)
    {
        Assert.IsFalse(DroppedElementStandIn.IsTypeName(typeName));
    }

    [TestMethod]
    public void TheStandInSurvivesVisualStudiosSerializer()
    {
        XamlAction[] actions = [new CreateInstanceAction(-5, DroppedElementStandIn.TypeName), new AddChildAction(-3, 4, -5)];

        IList<XamlAction> received = XamlActionJsonSerializer.Deserialize(XamlActionJsonSerializer.Serialize(actions));

        Assert.AreEqual(2, received.Count);
        var create = (CreateInstanceAction)received[0];
        Assert.AreEqual(-5L, create.LiveHandle);
        Assert.IsTrue(DroppedElementStandIn.IsTypeName(create.TypeName), create.TypeName);
        var add = (AddChildAction)received[1];
        Assert.AreEqual(-3L, add.ParentHandle);
        Assert.AreEqual(4, add.Index);
        Assert.AreEqual(-5L, add.ChildHandle);
    }
}
