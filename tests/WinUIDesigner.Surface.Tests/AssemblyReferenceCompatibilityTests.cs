// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinUIDesigner.Surface.Runtime;

[TestClass]
public sealed class AssemblyReferenceCompatibilityTests
{
    // A surface like the one that previewed the Ecierge solution: projections, the runtime they need, and .NET.
    private static readonly SurfaceAssemblies _CarriedBySurface = CarriedBySurface(
        ("Microsoft.Web.WebView2.Core.Projection", "1.0.3967.48"),
        ("Microsoft.Windows.SDK.NET", "10.0.28000.86"),
        ("Microsoft.Windows.ApplicationModel.Resources.Projection", "2.0.0.0"),
        ("WinRT.Runtime", "2.3.1.0"),
        ("Microsoft.WinUI", "3.0.0.0"),
        ("System.Text.Json", "10.0.0.0")
    );

    // The project's staged files, which have copies of assemblies the surface carries as well.
    private static readonly HashSet<string> _Staged = new(
        ["Ecierge.Console.UI", "Microsoft.Web.WebView2.Core.Projection", "Microsoft.Windows.SDK.NET", "WinRT.Runtime", "Microsoft.WinUI", "System.Text.Json"],
        StringComparer.OrdinalIgnoreCase
    );

    private static Func<string, Version?> Surface(params (string Name, string Version)[] assemblies)
    {
        var versions = assemblies.ToDictionary(item => item.Name, item => new Version(item.Version), StringComparer.OrdinalIgnoreCase);
        return name => versions.TryGetValue(name, out Version? version) ? version : null;
    }

    private static SurfaceAssemblies CarriedBySurface(params (string Name, string Version)[] assemblies)
    {
        var versions = assemblies.ToDictionary(item => item.Name, item => new Version(item.Version), StringComparer.OrdinalIgnoreCase);
        return new SurfaceAssemblies(
            string.Join(Path.PathSeparator, assemblies.Select(item => $@"C:\Surface\{item.Name}.dll")),
            path => versions[Path.GetFileNameWithoutExtension(path)]
        );
    }

    private static ReferenceResolution Resolve(string reference)
    {
        return AssemblyReferenceCompatibility.Resolve(new AssemblyName(reference), _CarriedBySurface, _Staged.Contains);
    }

    [TestMethod]
    public void ReferenceToANewerVersionThanTheSurfaceCarriesIsReported()
    {
        // A project built for Windows SDK 10.0.28000 against a surface that carries the 19041 projection.
        var references = new[] { new AssemblyReferenceInfo("Microsoft.Windows.SDK.NET", new Version(10, 0, 28000, 87)) };

        IReadOnlyList<UnsatisfiedAssemblyReference> findings = AssemblyReferenceCompatibility.FindUnsatisfied(
            "Ecierge.Uno.App",
            references,
            Surface(("Microsoft.Windows.SDK.NET", "10.0.19041.38")));

        UnsatisfiedAssemblyReference finding = Assert.ContainsSingle(findings);
        Assert.AreEqual("Ecierge.Uno.App", finding.Assembly);
        Assert.AreEqual("Microsoft.Windows.SDK.NET", finding.Reference);
        Assert.AreEqual(new Version(10, 0, 28000, 87), finding.Required);
        Assert.AreEqual(new Version(10, 0, 19041, 38), finding.Available);
    }

    [TestMethod]
    [DataRow("10.0.28000.87", "10.0.28000.87")]
    [DataRow("10.0.28000.87", "10.0.19041.38")]
    [DataRow("3.0.0.0", "3.0.0.0")]
    public void ReferenceTheSurfaceCanSatisfyIsNotReported(string available, string referenced)
    {
        var references = new[] { new AssemblyReferenceInfo("Microsoft.Windows.SDK.NET", new Version(referenced)) };

        IReadOnlyList<UnsatisfiedAssemblyReference> findings = AssemblyReferenceCompatibility.FindUnsatisfied(
            "Project.Assembly",
            references,
            Surface(("Microsoft.Windows.SDK.NET", available)));

        Assert.IsEmpty(findings);
    }

    [TestMethod]
    public void ReferenceToAnAssemblyTheSurfaceDoesNotCarryIsNotReported()
    {
        // Such an assembly is resolved from the project's own files.
        var references = new[] { new AssemblyReferenceInfo("Uno.Toolkit.WinUI", new Version(255, 255, 255, 255)) };

        IReadOnlyList<UnsatisfiedAssemblyReference> findings = AssemblyReferenceCompatibility.FindUnsatisfied(
            "Ecierge.Uno.App",
            references,
            Surface(("Microsoft.Windows.SDK.NET", "10.0.19041.38")));

        Assert.IsEmpty(findings);
    }

    [TestMethod]
    public void NamesAreComparedWithoutCase()
    {
        // The file of the surface's copy is spelled differently from the name in the reference.
        var surface = new SurfaceAssemblies(@"C:\Surface\microsoft.windows.sdk.net.dll", _ => new Version(10, 0, 19041, 38));
        var references = new[] { new AssemblyReferenceInfo("Microsoft.Windows.SDK.NET", new Version(10, 0, 28000, 87)) };

        IReadOnlyList<UnsatisfiedAssemblyReference> findings = AssemblyReferenceCompatibility.FindUnsatisfied("Ecierge.Uno.App", references, surface.GetVersion);

        UnsatisfiedAssemblyReference finding = Assert.ContainsSingle(findings);
        Assert.AreEqual(new Version(10, 0, 19041, 38), finding.Available);
    }

    [TestMethod]
    public void OnlyTheReferencesTheSurfaceCannotSatisfyAreReported()
    {
        var references = new[]
        {
            new AssemblyReferenceInfo("Microsoft.WinUI", new Version(3, 0, 0, 0)),
            new AssemblyReferenceInfo("Microsoft.Windows.SDK.NET", new Version(10, 0, 26100, 87)),
            new AssemblyReferenceInfo("WinRT.Runtime", new Version(2, 3, 1, 0)),
            new AssemblyReferenceInfo("System.Text.Json", new Version(11, 0, 0, 0)),
        };

        IReadOnlyList<UnsatisfiedAssemblyReference> findings = AssemblyReferenceCompatibility.FindUnsatisfied(
            "Project.Assembly",
            references,
            Surface(
                ("Microsoft.WinUI", "3.0.0.0"),
                ("Microsoft.Windows.SDK.NET", "10.0.19041.38"),
                ("WinRT.Runtime", "2.3.1.0"),
                ("System.Text.Json", "10.0.0.0")));

        CollectionAssert.AreEquivalent(
            new[] { "Microsoft.Windows.SDK.NET", "System.Text.Json" },
            findings.Select(finding => finding.Reference).ToArray());
    }

    [TestMethod]
    public void FindingsAreDescribedOncePerReferencedAssemblyAndVersion()
    {
        var findings = new[]
        {
            new UnsatisfiedAssemblyReference("Ecierge.Uno.Navigation", "Microsoft.Windows.SDK.NET", new Version(10, 0, 28000, 87), new Version(10, 0, 19041, 38)),
            new UnsatisfiedAssemblyReference("Ecierge.Uno.App", "Microsoft.Windows.SDK.NET", new Version(10, 0, 28000, 87), new Version(10, 0, 19041, 38)),
            new UnsatisfiedAssemblyReference("Ecierge.Uno.App", "System.Text.Json", new Version(11, 0, 0, 0), new Version(10, 0, 0, 0)),
        };

        IReadOnlyList<string> lines = AssemblyReferenceCompatibility.Describe(findings);

        Assert.HasCount(2, lines);
        StringAssert.StartsWith(lines[0], "Microsoft.Windows.SDK.NET 10.0.28000.87 is referenced by Ecierge.Uno.App, Ecierge.Uno.Navigation, but the design surface carries 10.0.19041.38");
        StringAssert.StartsWith(lines[1], "System.Text.Json 11.0.0.0 is referenced by Ecierge.Uno.App, but the design surface carries 10.0.0.0");
    }

    [TestMethod]
    public void ALongListOfAssembliesIsShortened()
    {
        UnsatisfiedAssemblyReference[] findings = Enumerable.Range(1, 8)
            .Select(index => new UnsatisfiedAssemblyReference($"Project.Part{index}", "Microsoft.Windows.SDK.NET", new Version(10, 0, 28000, 87), new Version(10, 0, 19041, 38)))
            .ToArray();

        string line = Assert.ContainsSingle(AssemblyReferenceCompatibility.Describe(findings));

        StringAssert.Contains(line, "Project.Part1, Project.Part2, Project.Part3, Project.Part4, Project.Part5 and 3 more");
    }

    [TestMethod]
    public void NoFindingsAreDescribedAsNothing()
    {
        Assert.IsEmpty(AssemblyReferenceCompatibility.Describe([]));
    }

    [TestMethod]
    public void ANewerProjectionIsDescribedAsServedByTheSurfaceCopy()
    {
        var findings = new[]
        {
            new UnsatisfiedAssemblyReference("Ecierge.Console.UI", "Microsoft.Web.WebView2.Core.Projection", new Version(1, 0, 3967, 48), new Version(1, 0, 3719, 77)),
            new UnsatisfiedAssemblyReference("Ecierge.Console.Management", "Microsoft.Web.WebView2.Core.Projection", new Version(1, 0, 3967, 48), new Version(1, 0, 3719, 77)),
            new UnsatisfiedAssemblyReference("Ecierge.Uno.App", "Microsoft.Windows.SDK.NET", new Version(10, 0, 29000, 0), new Version(10, 0, 28000, 86)),
        };

        IReadOnlyList<string> lines = AssemblyReferenceCompatibility.Describe(findings);

        Assert.HasCount(2, lines);
        Assert.StartsWith("Microsoft.Web.WebView2.Core.Projection 1.0.3967.48 is referenced by Ecierge.Console.Management, Ecierge.Console.UI, but the design surface carries 1.0.3719.77; ", lines[0]);
        Assert.StartsWith("Microsoft.Windows.SDK.NET 10.0.29000.0 is referenced by Ecierge.Uno.App, but the design surface carries 10.0.28000.86; ", lines[1]);
        foreach (string line in lines)
        {
            Assert.Contains("the runtime does not bind a reference to an older copy, so the surface serves it with its own copy", line);
            Assert.DoesNotContain("cannot be loaded", line);
        }
    }

    [TestMethod]
    public void ANewerReferenceTheSurfaceDoesNotServeIsDescribedAsFailing()
    {
        UnsatisfiedAssemblyReference[] findings = [new("Contoso.Controls", "WinRT.Runtime", new Version(2, 4, 0, 0), new Version(2, 3, 1, 0))];

        string line = Assert.ContainsSingle(AssemblyReferenceCompatibility.Describe(findings));

        Assert.StartsWith("WinRT.Runtime 2.4.0.0 is referenced by Contoso.Controls, but the design surface carries 2.3.1.0; ", line);
        Assert.Contains("the runtime does not bind a reference to an older copy, so the code of these assemblies that needs it fails", line);
        Assert.DoesNotContain("serves", line);
    }

    [TestMethod]
    [DataRow("Microsoft.Web.WebView2.Core.Projection, Version=1.0.4000.0, Culture=neutral, PublicKeyToken=null")]
    [DataRow("Microsoft.Windows.SDK.NET, Version=10.0.29000.0, Culture=neutral, PublicKeyToken=null")]
    [DataRow("microsoft.windows.sdk.net, Version=10.0.29000.0, Culture=neutral, PublicKeyToken=null")]
    [DataRow("Microsoft.Windows.ApplicationModel.Resources.Projection, Version=2.1.0.0, Culture=neutral, PublicKeyToken=null")]
    public void ANewerReferenceToACarriedProjectionIsServedByTheSurfaceCopy(string reference)
    {
        Assert.AreEqual(ReferenceResolution.SurfaceCopy, Resolve(reference));
    }

    [TestMethod]
    [DataRow("WinRT.Runtime, Version=2.4.0.0, Culture=neutral, PublicKeyToken=99ea127f02d97709")]
    [DataRow("Microsoft.WinUI, Version=3.1.0.0, Culture=neutral, PublicKeyToken=null")]
    [DataRow("System.Text.Json, Version=11.0.0.0, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51")]
    public void ANewerReferenceToAnotherCarriedAssemblyIsNotServed(string reference)
    {
        // The project has a copy of each of them, which is not loaded either.
        Assert.AreEqual(ReferenceResolution.None, Resolve(reference));
    }

    [TestMethod]
    [DataRow("Microsoft.Web.WebView2.Core.Projection")]
    [DataRow("Microsoft.Web.WebView2.Core.Projection, Version=1.0.3967.48, Culture=neutral, PublicKeyToken=null")]
    [DataRow("Microsoft.Web.WebView2.Core.Projection, Version=1.0.3719.77, Culture=neutral, PublicKeyToken=null")]
    public void AReferenceTheSurfaceCopySatisfiesIsLeftToTheRuntime(string reference)
    {
        // The runtime binds these to the surface's copy by itself; the project's staged copy is never loaded.
        Assert.AreEqual(ReferenceResolution.None, Resolve(reference));
    }

    [TestMethod]
    public void AnAssemblyOfTheProjectIsLoadedFromItsStagedCopy()
    {
        Assert.AreEqual(ReferenceResolution.ProjectCopy, Resolve("Ecierge.Console.UI, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null"));
    }

    [TestMethod]
    public void AnUnknownAssemblyIsNotResolved()
    {
        Assert.AreEqual(ReferenceResolution.None, Resolve("Contoso.Unknown, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null"));
    }

    [TestMethod]
    [DataRow("Microsoft.Windows.SDK.NET", true)]
    [DataRow("Microsoft.Web.WebView2.Core.Projection", true)]
    [DataRow("Microsoft.InteractiveExperiences.Projection", true)]
    [DataRow("WinRT.Runtime", false)]
    [DataRow("Microsoft.WinUI", false)]
    [DataRow("Microsoft.WindowsAppRuntime.Bootstrap.Net", false)]
    [DataRow("System.Text.Json", false)]
    public void OnlyTheProjectionsOfWinRTComponentsAreServedByAnOlderCopy(string simpleName, bool served)
    {
        Assert.AreEqual(served, AssemblyReferenceCompatibility.IsServedBySurfaceCopy(simpleName));
    }
}
