// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinUIDesigner.Surface.Runtime;

[TestClass]
public sealed class SurfaceAssembliesTests
{
    private static string TrustedPlatformAssemblies(params string[] paths) => string.Join(Path.PathSeparator, paths);

    [TestMethod]
    public void TheSurfaceCarriesItsTrustedPlatformAssemblies()
    {
        var surface = new SurfaceAssemblies(
            TrustedPlatformAssemblies(@"C:\Surface\Microsoft.Windows.SDK.NET.dll", @"C:\Surface\WinRT.Runtime.dll"),
            _ => new Version(1, 0)
        );

        Assert.IsTrue(surface.Contains("Microsoft.Windows.SDK.NET"));
        Assert.IsTrue(surface.Contains("winrt.runtime"));
        Assert.IsFalse(surface.Contains("Ecierge.Uno.App"));
    }

    [TestMethod]
    public void TheVersionIsReadFromTheSurfaceCopy()
    {
        var read = new List<string>();
        var surface = new SurfaceAssemblies(
            TrustedPlatformAssemblies(@"C:\Surface\Microsoft.Windows.SDK.NET.dll", @"C:\Surface\WinRT.Runtime.dll"),
            path =>
            {
                read.Add(path);
                return new Version(10, 0, 28000, 86);
            }
        );

        Assert.AreEqual(new Version(10, 0, 28000, 86), surface.GetVersion("MICROSOFT.WINDOWS.SDK.NET"));
        Assert.AreEqual(new Version(10, 0, 28000, 86), surface.GetVersion("Microsoft.Windows.SDK.NET"));
        Assert.IsNull(surface.GetVersion("Ecierge.Uno.App"));
        Assert.AreEqual(@"C:\Surface\Microsoft.Windows.SDK.NET.dll", Assert.ContainsSingle(read));
    }

    [TestMethod]
    public void AnUnreadableVersionIsNone()
    {
        var surface = new SurfaceAssemblies(@"C:\Surface\Microsoft.ML.OnnxRuntime.dll", _ => null);

        Assert.IsTrue(surface.Contains("Microsoft.ML.OnnxRuntime"));
        Assert.IsNull(surface.GetVersion("Microsoft.ML.OnnxRuntime"));
    }

    [TestMethod]
    public void AReaderThatThrowsGivesNoVersionAndIsNotAskedAgain()
    {
        // As when the host denies access to the surface's own file.
        int reads = 0;
        var surface = new SurfaceAssemblies(
            @"C:\Surface\Microsoft.Windows.SDK.NET.dll",
            _ =>
            {
                reads++;
                throw new UnauthorizedAccessException();
            }
        );

        Assert.IsTrue(surface.Contains("Microsoft.Windows.SDK.NET"));
        Assert.IsNull(surface.GetVersion("Microsoft.Windows.SDK.NET"));
        Assert.IsNull(surface.GetVersion("Microsoft.Windows.SDK.NET"));
        Assert.AreEqual(1, reads);
    }

    [TestMethod]
    public void WithoutTrustedPlatformAssembliesTheSurfaceCarriesNothing()
    {
        var surface = new SurfaceAssemblies(null, _ => new Version(1, 0));

        Assert.IsFalse(surface.Contains("Microsoft.Windows.SDK.NET"));
        Assert.IsNull(surface.GetVersion("Microsoft.Windows.SDK.NET"));
    }
}
