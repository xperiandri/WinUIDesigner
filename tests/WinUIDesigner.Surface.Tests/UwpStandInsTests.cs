// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinUIDesigner.Surface.Common;

[TestClass]
public sealed class UwpStandInsTests
{
    [TestMethod]
    // The names that Visual Studio's UWP instance builders send as the type of a CreateInstanceAction, and as the
    // declaring type of the properties of the stand-in (MinWindowWidth:XSurfUwp.AdaptiveTrigger, UwpSurface).
    [DataRow("XSurfUwp.AdaptiveTrigger, UwpSurface", "Microsoft.UI.Xaml.AdaptiveTrigger")]
    [DataRow("XSurfUwp.DesignInstance, UwpSurface", "XSurfUwp.DesignInstance")]
    [DataRow("XSurfUwp.Fallback.FallbackValueConverter, UwpSurface", "XSurfUwp.Fallback.FallbackValueConverter")]
    [DataRow("XSurfUwp.Fallback.FallbackDataTemplateSelector, UwpSurface", "XSurfUwp.Fallback.FallbackDataTemplateSelector")]
    [DataRow("XSurfUwp.Fallback.FallbackGroupStyleSelector, UwpSurface", "XSurfUwp.Fallback.FallbackGroupStyleSelector")]
    [DataRow("XSurfUwp.Fallback.FallbackStyleSelector, UwpSurface", "XSurfUwp.Fallback.FallbackStyleSelector")]
    public void StandInThatVisualStudioSendsMapsToTheTypeThatTheSurfaceCreates(string serializedTypeName, string runtimeTypeName)
    {
        Assert.AreEqual(runtimeTypeName, UwpStandIns.GetRuntimeTypeName(serializedTypeName));
    }

    [TestMethod]
    [DataRow("XSurfUwp.AdaptiveTrigger")]
    [DataRow("  XSurfUwp.AdaptiveTrigger ,UwpSurface ")]
    [DataRow("XSurfUwp.AdaptiveTrigger, UwpSurface, Version=17.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a")]
    public void AssemblyPartAndSurroundingSpaceDoNotMatter(string serializedTypeName)
    {
        Assert.AreEqual("Microsoft.UI.Xaml.AdaptiveTrigger", UwpStandIns.GetRuntimeTypeName(serializedTypeName));
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow(" ")]
    // The trigger of WinUI itself, and a type of the project with the same short name.
    [DataRow("Microsoft.UI.Xaml.AdaptiveTrigger, Microsoft.WinUI")]
    [DataRow("Ecierge.AdaptiveTrigger, Ecierge")]
    // The owner of the design-time properties is a type of this surface that is never created.
    [DataRow("XSurfUwp.DT")]
    // The fallback controls become a ContentControl in ResolveType itself.
    [DataRow("XSurfUwp.Fallback.FallbackControl, UwpSurface")]
    [DataRow("XSurfUwp.Fallback.LayoutFaultFallbackControl, UwpSurface")]
    // Type names are case-sensitive, and a longer name is another type.
    [DataRow("xsurfuwp.adaptivetrigger, UwpSurface")]
    [DataRow("XSurfUwp.AdaptiveTriggers, UwpSurface")]
    // The protocol name of a resource reference is not a stand-in of the UWP surface.
    [DataRow("System.Windows.StaticResourceExtension")]
    public void OtherNamesAreNoStandIns(string? serializedTypeName)
    {
        Assert.IsNull(UwpStandIns.GetRuntimeTypeName(serializedTypeName));
    }

    [TestMethod]
    public void PreparedAdaptiveTriggerLoadsAsTheTriggerOfWinUI()
    {
        Assert.AreEqual(
            XName.Get("AdaptiveTrigger", "http://schemas.microsoft.com/winfx/2006/xaml/presentation"),
            UwpStandIns.GetRuntimeElementName(XName.Get("AdaptiveTrigger", "using:XSurfUwp"))
        );
    }

    [TestMethod]
    // The trigger of WinUI already has the right name.
    [DataRow("AdaptiveTrigger", "http://schemas.microsoft.com/winfx/2006/xaml/presentation")]
    // Another element of the stand-in namespace, and a fallback control, which BuildDocumentAsync renames itself.
    [DataRow("StateTrigger", "using:XSurfUwp")]
    [DataRow("FallbackControl", "using:XSurfUwp.Fallback")]
    // A trigger of the project with the same name.
    [DataRow("AdaptiveTrigger", "using:Ecierge.Triggers")]
    public void OtherElementsKeepTheirNames(string localName, string namespaceName)
    {
        Assert.IsNull(UwpStandIns.GetRuntimeElementName(XName.Get(localName, namespaceName)));
    }

    [TestMethod]
    public void RenamedTriggerIsWrittenWithThePrefixOfThePresentationNamespace()
    {
        // The cleaner writes the stand-in with a prefix of its own namespace; the reader does not know that namespace.
        XDocument document = XDocument.Parse(
            """
            <Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:xsurf="using:XSurfUwp">
              <VisualStateManager.VisualStateGroups>
                <VisualStateGroup>
                  <VisualState>
                    <VisualState.StateTriggers>
                      <xsurf:AdaptiveTrigger MinWindowWidth="700" />
                    </VisualState.StateTriggers>
                  </VisualState>
                </VisualStateGroup>
              </VisualStateManager.VisualStateGroups>
            </Grid>
            """
        );

        UwpStandIns.RenameStandIns(document.Root!);

        string xaml = document.ToString(SaveOptions.DisableFormatting);
        Assert.Contains("<AdaptiveTrigger MinWindowWidth=\"700\" />", xaml);
        Assert.DoesNotContain("xsurf:AdaptiveTrigger", xaml);
    }

    [TestMethod]
    public void RootThatIsAStandInIsRenamedToo()
    {
        var root = XElement.Parse("""<xsurf:AdaptiveTrigger xmlns:xsurf="using:XSurfUwp" MinWindowHeight="630" />""");

        UwpStandIns.RenameStandIns(root);

        Assert.AreEqual(XName.Get("AdaptiveTrigger", "http://schemas.microsoft.com/winfx/2006/xaml/presentation"), root.Name);
    }

    [TestMethod]
    public void RenamingTheStandInsLeavesTheOtherElementsAlone()
    {
        // The trigger of WinUI, a trigger of the project with the same name, and another element of the stand-in namespace.
        XDocument document = XDocument.Parse(
            """
            <Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:xsurf="using:XSurfUwp" xmlns:local="using:Ecierge.Triggers">
              <AdaptiveTrigger MinWindowWidth="700" />
              <local:AdaptiveTrigger MinWindowWidth="700" />
              <xsurf:StateTrigger />
            </Grid>
            """
        );
        string expected = document.ToString(SaveOptions.DisableFormatting);

        UwpStandIns.RenameStandIns(document.Root!);

        Assert.AreEqual(expected, document.ToString(SaveOptions.DisableFormatting));
    }
}
