// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using Mono.Cecil;

/// <summary>
/// Checks, in the built design surface, the types that take the place of the stand-ins of Visual Studio's UWP surface
/// (<c>UwpStandInInfo</c>): Visual Studio names them in the assembly <c>UwpSurface</c>, and the surface creates the
/// type of the same full name, or the WinUI trigger for <c>XSurfUwp.AdaptiveTrigger</c>.
/// </summary>
[TestClass]
public sealed class SurfaceStandInTypesTests
{
    private const string _DesignInstance = "XSurfUwp.DesignInstance";

    [TestMethod]
    // The stand-ins that the surface defines itself, and the WinUI type that each one is.
    [DataRow(_DesignInstance, "Microsoft.UI.Xaml.Markup.MarkupExtension")]
    [DataRow("XSurfUwp.Fallback.FallbackDataTemplateSelector", "Microsoft.UI.Xaml.Controls.DataTemplateSelector")]
    [DataRow("XSurfUwp.Fallback.FallbackGroupStyleSelector", "Microsoft.UI.Xaml.Controls.GroupStyleSelector")]
    [DataRow("XSurfUwp.Fallback.FallbackStyleSelector", "Microsoft.UI.Xaml.Controls.StyleSelector")]
    public void SurfaceDefinesTheStandInUnderTheNameOfTheUwpSurface(string fullName, string baseTypeName)
    {
        using AssemblyDefinition surface = AssemblyDefinition.ReadAssembly(FindSurfaceOutput("WinUISurface.dll"));

        TypeDefinition type = RequireCreatableType(surface, fullName);

        Assert.AreEqual(baseTypeName, type.BaseType?.FullName, $"Base type of {fullName}");
    }

    [TestMethod]
    public void FallbackValueConverterIsAValueConverter()
    {
        using AssemblyDefinition surface = AssemblyDefinition.ReadAssembly(FindSurfaceOutput("WinUISurface.dll"));

        TypeDefinition type = RequireCreatableType(surface, "XSurfUwp.Fallback.FallbackValueConverter");

        Assert.Contains(implementation => implementation.InterfaceType.FullName == "Microsoft.UI.Xaml.Data.IValueConverter", type.Interfaces);
    }

    [TestMethod]
    // The members that UwpDesignInstanceExtensionBuilder sets; Type arrives as an assembly-qualified name.
    [DataRow("Type", "System.Object")]
    [DataRow("IsDesignTimeCreatable", "System.Boolean")]
    [DataRow("CreateList", "System.Boolean")]
    public void DesignInstanceHasTheMembersThatVisualStudioSets(string propertyName, string propertyTypeName)
    {
        using AssemblyDefinition surface = AssemblyDefinition.ReadAssembly(FindSurfaceOutput("WinUISurface.dll"));

        RequireWritableProperty(RequireCreatableType(surface, _DesignInstance), propertyName, propertyTypeName);
    }

    [TestMethod]
    public void DesignInstanceProvidesItsValueToTheProperty()
    {
        using AssemblyDefinition surface = AssemblyDefinition.ReadAssembly(FindSurfaceOutput("WinUISurface.dll"));

        TypeDefinition type = RequireCreatableType(surface, _DesignInstance);

        // The surface evaluates a markup extension through MarkupExtension.ProvideValue() before it sets a property.
        Assert.Contains(method => method.Name == "ProvideValue" && method.IsVirtual && !method.IsNewSlot && !method.HasParameters, type.Methods);
    }

    [TestMethod]
    // The only members that Visual Studio builds on the AdaptiveTrigger stand-in.
    [DataRow("MinWindowWidth")]
    [DataRow("MinWindowHeight")]
    public void TriggerOfWinUIHasTheMembersOfTheAdaptiveTriggerStandIn(string propertyName)
    {
        using AssemblyDefinition projection = AssemblyDefinition.ReadAssembly(FindSurfaceOutput("Microsoft.WinUI.dll"));

        RequireWritableProperty(RequireCreatableType(projection, "Microsoft.UI.Xaml.AdaptiveTrigger"), propertyName, "System.Double");
    }

    private static TypeDefinition RequireCreatableType(AssemblyDefinition assembly, string fullName)
    {
        TypeDefinition? type = assembly.MainModule.GetType(fullName);
        Assert.IsNotNull(type, $"{assembly.Name.Name} must define {fullName}.");
        Assert.IsTrue(type.IsPublic && !type.IsAbstract && !type.HasGenericParameters, $"{fullName} must be a public concrete type.");
        Assert.Contains(
            method => method.IsConstructor && method.IsPublic && !method.IsStatic && !method.HasParameters,
            type.Methods,
            $"{fullName} needs a public parameterless constructor."
        );
        return type;
    }

    private static void RequireWritableProperty(TypeDefinition type, string propertyName, string propertyTypeName)
    {
        PropertyDefinition? property = type.Properties.FirstOrDefault(candidate => candidate.Name == propertyName);
        Assert.IsNotNull(property, $"{type.FullName} must have the property {propertyName}.");
        Assert.AreEqual(propertyTypeName, property.PropertyType.FullName, $"Type of {type.FullName}.{propertyName}");
        Assert.IsTrue(property.SetMethod is { IsPublic: true, IsStatic: false }, $"{type.FullName}.{propertyName} must have a public setter.");
    }

    // The surface is built into src/WinUIDesigner.Surface/bin/Release/<target framework>/<runtime>, net10.0 for Visual
    // Studio 18 and net8.0 for Visual Studio 17.14; the newest build wins.
    private static string FindSurfaceOutput(string fileName)
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (!File.Exists(Path.Combine(directory.FullName, "WinUIDesigner.slnx")))
            {
                continue;
            }

            string surfaceOutputDirectory = Path.Combine(directory.FullName, "src", "WinUIDesigner.Surface", "bin", "Release");
            string? path = Directory.Exists(surfaceOutputDirectory)
                ? Directory.EnumerateFiles(surfaceOutputDirectory, fileName, SearchOption.AllDirectories)
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault()
                : null;
            return path ?? throw new FileNotFoundException($"Could not locate {fileName} under '{surfaceOutputDirectory}'. Build WinUIDesigner.Surface first.");
        }

        throw new FileNotFoundException("Could not locate WinUIDesigner.slnx from the test output directory.");
    }
}
