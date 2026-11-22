// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinUIDesigner.Surface.Runtime;

[TestClass]
public sealed class AssemblyReferenceCompatibilityTests
{
    private static Func<string, Version?> Surface(params (string Name, string Version)[] assemblies)
    {
        var versions = assemblies.ToDictionary(item => item.Name, item => new Version(item.Version), StringComparer.OrdinalIgnoreCase);
        return name => versions.TryGetValue(name, out Version? version) ? version : null;
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
}
