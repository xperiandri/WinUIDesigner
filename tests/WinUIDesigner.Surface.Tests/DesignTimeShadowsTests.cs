// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using WinUIDesigner.Surface.Common;

[TestClass]
public sealed class DesignTimeShadowsTests
{
    [TestMethod]
    [DataRow("RuntimeVisibility:XSurfUwp.DT", "Visibility")]
    [DataRow("RuntimeOpacity:XSurfUwp.DT", "Opacity")]
    [DataRow("RuntimeIsHitTestVisible:XSurfUwp.DT", "IsHitTestVisible")]
    [DataRow("RuntimeDataContext:XSurfUwp.DT", "DataContext")]
    [DataRow("RuntimeIsAppBarOpen:XSurfUwp.DT", "IsOpen")]
    [DataRow("RuntimeIsPopupOpen:XSurfUwp.DT", "IsOpen")]
    [DataRow("RuntimeExpanderIsExpanded:XSurfUwp.DT", "IsExpanded")]
    [DataRow("RuntimeIsSplitViewPaneOpen:XSurfUwp.DT", "IsPaneOpen")]
    [DataRow("RuntimeFontWeight:XSurfUwp.DT", "FontWeight")]
    public void ShadowStandsForTheProperty(string fullPropertyName, string expected)
    {
        Assert.IsTrue(DesignTimeShadows.TryGetShadowedProperty(fullPropertyName, out string propertyName));
        Assert.AreEqual(expected, propertyName);
    }

    [TestMethod]
    public void DeclaringTypeMayCarryAnAssemblyName()
    {
        Assert.IsTrue(DesignTimeShadows.TryGetShadowedProperty("RuntimeVisibility:XSurfUwp.DT, WinUISurface, Version=1.0.0.0", out string propertyName));
        Assert.AreEqual("Visibility", propertyName);
    }

    [TestMethod]
    [DataRow("RuntimeVisibility:Microsoft.UI.Xaml.UIElement, Microsoft.WinUI, Version=3.0.0.0")]
    [DataRow("RuntimeVisibility:XSurfUwp.Other")]
    [DataRow("RuntimeVisibility:XSurfUwp.DT2")]
    public void PropertyOfAnotherTypeIsNoShadow(string fullPropertyName)
    {
        Assert.IsFalse(DesignTimeShadows.TryGetShadowedProperty(fullPropertyName, out string propertyName));
        Assert.AreEqual(string.Empty, propertyName);
    }

    [TestMethod]
    [DataRow("RuntimeWidth:XSurfUwp.DT")]
    [DataRow("RuntimeHeight:XSurfUwp.DT")]
    [DataRow("DesignWidth:XSurfUwp.DT")]
    public void SizeShadowsBelongToTheDesignerType(string fullPropertyName)
    {
        Assert.IsFalse(DesignTimeShadows.TryGetShadowedProperty(fullPropertyName, out _));
    }

    [TestMethod]
    [DataRow("RuntimeNoSuchThing:XSurfUwp.DT")]
    [DataRow("Visibility:XSurfUwp.DT")]
    [DataRow("RuntimeEffect:XSurfUwp.DT")]
    public void UnknownNameIsNoShadow(string fullPropertyName)
    {
        Assert.IsFalse(DesignTimeShadows.TryGetShadowedProperty(fullPropertyName, out _));
    }

    [TestMethod]
    [DataRow("RuntimeVisibility")]
    [DataRow(":XSurfUwp.DT")]
    [DataRow("")]
    public void NameWithoutDeclaringTypeIsNoShadow(string fullPropertyName)
    {
        Assert.IsFalse(DesignTimeShadows.TryGetShadowedProperty(fullPropertyName, out _));
    }
}
