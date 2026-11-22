// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System.Runtime.Versioning;
using Microsoft.VisualStudio.DesignTools.Utility;
using WinUIDesigner.DesignerHost;
using WinUIDesigner.Platform;

[TestClass]
public sealed class PlatformSupportTests
{
    // The specification of Visual Studio's desktop WinUI configuration, which receives the host binding.
    private static readonly PlatformSpecification _DesktopWinUI = new(
        "TargetPlatformIdentifier=Windows;TargetPlatformVersion=10.0-..;TargetRuntime=Managed,Native;TargetFrameworkIdentifier=.NETCoreApp;TargetFrameworkVersion=5.0-..;XamlRuntime=WinUI"
    );

    private static readonly FrameworkName _Net10 = new(".NETCoreApp", new Version(10, 0));

    // net10.0-windows10.0.26100 of a WinUI or Uno project.
    private static PlatformIdentifier WindowsTarget => new(
        new PlatformName("Windows", new Version(10, 0, 26100, 0), new Version(10, 0, 26100, 0)),
        "Managed",
        _Net10,
        new SdkName("Windows", new Version(10, 0)),
        XamlRuntimeNames.WinUI
    );

    // net10.0-android of an Uno project: Uno.Sdk sets DefaultXamlRuntime=WinUI for every target framework.
    private static PlatformIdentifier AndroidTarget => new(
        new PlatformName("Android", new Version(36, 0)),
        "Managed",
        _Net10,
        XamlRuntimeNames.WinUI
    );

    // net10.0 of an Uno project: no target platform at all.
    private static PlatformIdentifier PlatformNeutralTarget => new(null, "Managed", _Net10, XamlRuntimeNames.WinUI);

    [TestCleanup]
    public void Cleanup() => WinUIPlatformSupport.Reset();

    [TestMethod]
    public void WindowsTargetOfWinUIProjectIsCovered() => Assert.IsTrue(WinUIPlatformSupport.Matches(WindowsTarget, _DesktopWinUI));

    [TestMethod]
    public void NonWindowsTargetsReportingWinUIRuntimeAreNotCovered()
    {
        Assert.IsFalse(WinUIPlatformSupport.Matches(AndroidTarget, _DesktopWinUI), "Android target");
        Assert.IsFalse(WinUIPlatformSupport.Matches(PlatformNeutralTarget, _DesktopWinUI), "Platform-neutral target");
    }

    [TestMethod]
    public void OtherXamlRuntimesAreNotCovered()
    {
        var wpf = new PlatformIdentifier(new PlatformName("Windows", new Version(7, 0)), "Managed", _Net10, XamlRuntimeNames.WpfCore);
        var unspecified = new PlatformIdentifier(new PlatformName("Windows", new Version(10, 0, 26100, 0)), "Managed", _Net10, null);

        Assert.IsFalse(WinUIPlatformSupport.Matches(wpf, _DesktopWinUI), "WPF");
        Assert.IsFalse(WinUIPlatformSupport.Matches(unspecified, _DesktopWinUI), "No XAML runtime");
    }

    [TestMethod]
    public void NothingIsHostedBeforeConfigurationOrAfterReset()
    {
        Assert.IsFalse(WinUIPlatformSupport.IsHosted(WindowsTarget));

        WinUIPlatformSupport.Configure(_DesktopWinUI);
        Assert.IsTrue(WinUIPlatformSupport.IsHosted(WindowsTarget));
        Assert.IsFalse(WinUIPlatformSupport.IsHosted(AndroidTarget));
        Assert.IsFalse(WinUIPlatformSupport.IsHosted(null));

        WinUIPlatformSupport.Reset();
        Assert.IsFalse(WinUIPlatformSupport.IsHosted(WindowsTarget));
    }

    [TestMethod]
    public void HostBindingProbeCanVetoACoveredIdentity()
    {
        int probed = 0;
        WinUIPlatformSupport.Configure(_DesktopWinUI, _ =>
        {
            probed++;
            return false;
        });

        Assert.IsFalse(WinUIPlatformSupport.IsHosted(WindowsTarget));
        Assert.AreEqual(1, probed);

        // An identity the configuration does not cover never reaches the probe.
        Assert.IsFalse(WinUIPlatformSupport.IsHosted(AndroidTarget));
        Assert.AreEqual(1, probed);
    }

    [TestMethod]
    public void SupportedProjectHasNoUnsupportedReason()
    {
        Assert.IsNull(WinUIProjectRequirements.GetUnsupportedReason(WindowsTarget, WinUIProjectRequirements.VerifiedWinUIRuntimeFileVersion));

        // A project without a Microsoft.WinUI.dll reference has nothing to verify.
        Assert.IsNull(WinUIProjectRequirements.GetUnsupportedReason(WindowsTarget, (string?)null));
    }

    [TestMethod]
    public void OtherWindowsAppSdkVersionIsExplained()
    {
        string? reason = WinUIProjectRequirements.GetUnsupportedReason(WindowsTarget, "3.0.0.2508");

        Assert.IsNotNull(reason);
        Assert.Contains(WinUIProjectRequirements.VerifiedWindowsAppSdkVersion, reason);
        Assert.Contains("3.0.0.2508", reason);

        // A file without version information cannot be verified either.
        AssertExplains("unknown version", WinUIProjectRequirements.GetUnsupportedReason(WindowsTarget, string.Empty));
    }

    [TestMethod]
    public void NonManagedAndNonCoreProjectsAreExplained()
    {
        var native = new PlatformIdentifier(new PlatformName("Windows", new Version(10, 0, 26100, 0)), "Native", _Net10, XamlRuntimeNames.WinUI);
        var framework = new PlatformIdentifier(
            new PlatformName("Windows", new Version(10, 0, 26100, 0)),
            "Managed",
            new FrameworkName(".NETFramework", new Version(4, 8)),
            XamlRuntimeNames.WinUI
        );

        AssertExplains("managed", WinUIProjectRequirements.GetUnsupportedReason(native, (string?)null));
        AssertExplains(".NETCoreApp", WinUIProjectRequirements.GetUnsupportedReason(framework, (string?)null));
        AssertExplains(".NETCoreApp", WinUIProjectRequirements.GetUnsupportedReason(null, (string?)null));
    }

    [TestMethod]
    public void WinUIRuntimeReferenceIsFoundByFileName()
    {
        string?[] references =
        [
            null,
            @"C:\packages\microsoft.windowsappsdk.winui\2.5.1\lib\net8.0-windows10.0.19041.0\Microsoft.InteractiveExperiences.Projection.dll",
            @"C:\packages\microsoft.windowsappsdk.winui\2.5.1\lib\net8.0-windows10.0.19041.0\microsoft.winui.DLL",
        ];

        Assert.AreEqual(references[2], WinUIProjectRequirements.FindWinUIRuntimePath(references));
        Assert.IsNull(WinUIProjectRequirements.FindWinUIRuntimePath([references[1]]));
        Assert.IsNull(WinUIProjectRequirements.FindWinUIRuntimePath(null));
    }

    [TestMethod]
    public void SurfaceIsLaunchedThroughACompleteContextOfAHostedProject()
    {
        WinUIPlatformSupport.Configure(_DesktopWinUI);

        Assert.IsNull(WinUISurfaceLaunch.GetBlocker(new FakeProject()));
    }

    [TestMethod]
    public void SurfaceLaunchBlockerNamesWhatTheContextLacks()
    {
        WinUIPlatformSupport.Configure(_DesktopWinUI);

        AssertExplains("no project context", WinUISurfaceLaunch.GetBlocker(null));
        AssertExplains("was closed", WinUISurfaceLaunch.GetBlocker(new FakeProject { IsDisposed = true }));
        AssertExplains("no WinUI process host", WinUISurfaceLaunch.GetBlocker(new FakeProject { PlatformIdentifier = AndroidTarget }));
        AssertExplains("no designer metadata", WinUISurfaceLaunch.GetBlocker(new FakeProject { HasMetadata = false }));
    }

    [TestMethod]
    public void SurfaceIsNotLaunchedThroughAContextWithoutItsProject()
    {
        WinUIPlatformSupport.Configure(_DesktopWinUI);

        // Visual Studio hands the project of the context to the process host without checking it.
        AssertExplains("has no project", WinUISurfaceLaunch.GetBlocker(new FakeProject { HasHostProject = false }));

        // Without its project the context reports no name and no platform identity either.
        var detached = new FakeProject { HasHostProject = false, ProjectName = null, PlatformIdentifier = null };
        AssertExplains("has no project", WinUISurfaceLaunch.GetBlocker(detached));
    }

    [TestMethod]
    public void SurfaceIsNotLaunchedWithoutThePlatformPartOfTheMetadata()
    {
        WinUIPlatformSupport.Configure(_DesktopWinUI);

        // Visual Studio reads the control assemblies to shadow-copy from it without checking it.
        AssertExplains("platform metadata", WinUISurfaceLaunch.GetBlocker(new FakeProject { HasPlatformMetadata = false }));
    }

    [TestMethod]
    public void LibrarySharesTheSurfaceProcessOfAnApplicationThatMeetsTheRequirements()
    {
        WinUIPlatformSupport.Configure(_DesktopWinUI);

        Assert.IsNull(WinUISurfaceLaunch.GetSharingBlocker(new FakeProject()));
    }

    [TestMethod]
    public void LibraryDoesNotShareTheSurfaceProcessOfAnApplicationOnAnotherWinUIRuntime()
    {
        WinUIPlatformSupport.Configure(_DesktopWinUI);

        // The process would run the application's Microsoft.WinUI.dll, here the one of Windows App SDK 2.4.0,
        // and the shadow-copy worker factory refuses to prepare it.
        AssertExplains("3.0.0.2608", WinUISurfaceLaunch.GetSharingBlocker(new FakeProject { WinUIRuntimeFileVersion = "3.0.0.2608" }));
    }

    [TestMethod]
    public void LibraryDoesNotShareTheSurfaceProcessOfAnApplicationThatCannotLaunchOne()
    {
        WinUIPlatformSupport.Configure(_DesktopWinUI);

        // An Uno app head whose active target is Android.
        AssertExplains("no WinUI process host", WinUISurfaceLaunch.GetSharingBlocker(new FakeProject { PlatformIdentifier = AndroidTarget }));
        AssertExplains("no project context", WinUISurfaceLaunch.GetSharingBlocker(null));
    }

    [TestMethod]
    public void LibraryIsCheckedAgainstTheWinUIRuntimeOfTheApplicationWhoseSurfaceProcessItUses()
    {
        WinUIPlatformSupport.Configure(_DesktopWinUI);

        // A library on Windows App SDK 1.8 binds to the 2.5.1 runtime of its application in that application's process.
        var library = new FakeProject { WinUIRuntimeFileVersion = "3.0.0.2602" };
        Assert.IsNull(WinUIProjectRequirements.GetUnsupportedReason(library, new FakeProject()));

        // Without such an application the library starts a surface process of its own, on its own runtime.
        AssertExplains("3.0.0.2602", WinUIProjectRequirements.GetUnsupportedReason(library, null));
    }

    [TestMethod]
    public void LibraryIsCheckedAgainstItsOwnWinUIRuntimeWhenItsApplicationCannotShare()
    {
        WinUIPlatformSupport.Configure(_DesktopWinUI);
        var library = new FakeProject { WinUIRuntimeFileVersion = "3.0.0.2602" };
        var applicationOnOtherRuntime = new FakeProject { WinUIRuntimeFileVersion = "3.0.0.2608" };
        var androidHead = new FakeProject { PlatformIdentifier = AndroidTarget };

        // An application on Windows App SDK 2.4.0, and an Uno app head whose active target is Android.
        AssertExplains("3.0.0.2602", WinUIProjectRequirements.GetUnsupportedReason(library, applicationOnOtherRuntime));
        AssertExplains("3.0.0.2602", WinUIProjectRequirements.GetUnsupportedReason(library, androidHead));

        // A surface process of its own previews a library on the verified runtime.
        Assert.IsNull(WinUIProjectRequirements.GetUnsupportedReason(new FakeProject(), applicationOnOtherRuntime));
    }

    [TestMethod]
    public void LibraryTargetCountsAlsoInTheSurfaceProcessOfItsApplication()
    {
        WinUIPlatformSupport.Configure(_DesktopWinUI);
        var native = new FakeProject
        {
            PlatformIdentifier = new PlatformIdentifier(
                new PlatformName("Windows", new Version(10, 0, 26100, 0)),
                "Native",
                _Net10,
                XamlRuntimeNames.WinUI
            ),
        };

        AssertExplains("managed", WinUIProjectRequirements.GetUnsupportedReason(native, new FakeProject()));
    }

    // Asserts that a reason is given and that it contains the expected text.
    private static void AssertExplains(string expected, string? reason)
    {
        Assert.IsNotNull(reason, $"Expected a reason that contains '{expected}'.");
        Assert.Contains(expected, reason);
    }

    // A complete project context of a hosted Windows target on the verified WinUI runtime; each test changes what
    // it is about.
    private sealed class FakeProject : ISurfaceLaunchContext
    {
        public string? ProjectName { get; set; } = "App";

        public bool IsDisposed { get; set; }

        public bool HasHostProject { get; set; } = true;

        public PlatformIdentifier? PlatformIdentifier { get; set; } = WindowsTarget;

        public bool HasMetadata { get; set; } = true;

        public bool HasPlatformMetadata { get; set; } = true;

        public string? WinUIRuntimeFileVersion { get; set; } = WinUIProjectRequirements.VerifiedWinUIRuntimeFileVersion;
    }
}
