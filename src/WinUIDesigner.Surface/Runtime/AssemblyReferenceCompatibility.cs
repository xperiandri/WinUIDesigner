// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace WinUIDesigner.Surface.Runtime;

/// <summary>
/// An assembly that another assembly references: its simple name and the version it was compiled against.
/// </summary>
internal readonly record struct AssemblyReferenceInfo(string Name, Version Version);

/// <summary>
/// A reference of a project assembly that the runtime cannot bind in the surface process.
/// </summary>
/// <param name="Assembly">The project assembly that holds the reference.</param>
/// <param name="Reference">The simple name of the referenced assembly.</param>
/// <param name="Required">The version the project assembly was compiled against.</param>
/// <param name="Available">The version of the assembly the surface carries under that name.</param>
internal readonly record struct UnsatisfiedAssemblyReference(string Assembly, string Reference, Version Required, Version Available);

/// <summary>
/// The copy of an assembly that serves a reference which the runtime could not bind by itself.
/// </summary>
internal enum ReferenceResolution
{
    /// <summary>
    /// No copy: the reference is left to the other handlers, and fails when none of them serves it.
    /// </summary>
    None,

    /// <summary>
    /// The surface's own copy, which is older than the referenced version.
    /// </summary>
    SurfaceCopy,

    /// <summary>
    /// The project's staged copy of an assembly the surface does not carry.
    /// </summary>
    ProjectCopy,
}

/// <summary>
/// Finds the references of the project's assemblies that the surface's own assemblies do not satisfy, and decides
/// which copy serves a reference that the runtime could not bind by itself.
/// </summary>
/// <remarks>
/// The surface carries its own copies of many assemblies, such as <c>Microsoft.Windows.SDK.NET</c>, the projection
/// of the Windows SDK. The runtime binds a reference to one of these names only to the surface's copy, and only when
/// that copy is not older than the referenced version: it does not bind the reference to an older copy, and it cannot
/// load the project's newer copy beside the surface's (<c>0x80131040</c>, "the located assembly's manifest definition
/// does not match the assembly reference"). The project's assemblies still load, but their code that needs such a
/// reference fails, and so does the XAML metadata provider of an assembly whose type table names one of its types.
/// The surface therefore serves a newer reference to the projection of a WinRT component with its own, older copy
/// (<see cref="Resolve"/>): a newer projection only adds types and members, so only the code that uses one of them
/// fails.
/// </remarks>
internal static class AssemblyReferenceCompatibility
{
    /// <summary>
    /// Returns the references of <paramref name="assembly"/> whose referenced version is newer than the version the
    /// surface carries.
    /// </summary>
    /// <param name="assembly">The name of the project assembly that holds the references.</param>
    /// <param name="references">The assembly references of that assembly.</param>
    /// <param name="getSurfaceVersion">
    /// Gets the version of the assembly the surface carries under a simple name, or <see langword="null"/> when it
    /// carries none; a reference to an assembly the surface does not carry is resolved from the project instead.
    /// </param>
    public static IReadOnlyList<UnsatisfiedAssemblyReference> FindUnsatisfied(
        string assembly,
        IEnumerable<AssemblyReferenceInfo> references,
        Func<string, Version?> getSurfaceVersion)
    {
        var unsatisfied = new List<UnsatisfiedAssemblyReference>();
        foreach (AssemblyReferenceInfo reference in references)
        {
            Version? available = getSurfaceVersion(reference.Name);
            if (available is not null && available < reference.Version)
            {
                unsatisfied.Add(new UnsatisfiedAssemblyReference(assembly, reference.Name, reference.Version, available));
            }
        }

        return unsatisfied;
    }

    /// <summary>
    /// Decides which copy of an assembly serves a reference that the runtime could not bind by itself.
    /// </summary>
    /// <param name="reference">The referenced assembly, with the version that was referenced.</param>
    /// <param name="surface">The assemblies the surface carries.</param>
    /// <param name="isStaged">Returns whether the project's staged files have an assembly of a simple name.</param>
    /// <returns>
    /// <see cref="ReferenceResolution.SurfaceCopy"/> for a newer reference to a WinRT projection that the surface
    /// carries (<see cref="IsServedBySurfaceCopy"/>), <see cref="ReferenceResolution.ProjectCopy"/> for a staged
    /// assembly that the surface does not carry, and <see cref="ReferenceResolution.None"/> otherwise. The project's
    /// copy of an assembly that the surface carries is never loaded: the runtime cannot load it beside the surface's.
    /// </returns>
    public static ReferenceResolution Resolve(AssemblyName reference, SurfaceAssemblies surface, Func<string, bool> isStaged)
    {
        // A name without a simple name.
        if (reference.Name is not { } name)
        {
            return ReferenceResolution.None;
        }

        // An assembly of the project, which the surface does not carry.
        if (!surface.Contains(name))
        {
            return isStaged(name) ? ReferenceResolution.ProjectCopy : ReferenceResolution.None;
        }

        // An assembly the surface carries that an older copy must not serve.
        if (!IsServedBySurfaceCopy(name))
        {
            return ReferenceResolution.None;
        }

        // A WinRT projection the surface carries. The runtime asks for it only when the reference is newer than the
        // surface's copy; a request without a version, or for one the copy satisfies, cannot be served by it.
        return reference.Version is { } requested && surface.GetVersion(name) is { } available && requested > available
            ? ReferenceResolution.SurfaceCopy
            : ReferenceResolution.None;
    }

    /// <summary>
    /// Returns whether the surface's own copy of an assembly may serve a reference to a newer version: the projection
    /// of a WinRT component, that is <c>Microsoft.Windows.SDK.NET</c>, the projection of the Windows SDK, and the
    /// <c>*.Projection</c> assemblies, such as those of WebView2 and of the Windows App SDK. A newer projection of a
    /// component only adds types and members. <c>WinRT.Runtime</c>, the runtime of the projections,
    /// <c>Microsoft.WinUI</c>, whose XAML framework is the surface's own, and the assemblies of .NET are not served by
    /// an older copy.
    /// </summary>
    public static bool IsServedBySurfaceCopy(string simpleName)
    {
        return simpleName.Equals("Microsoft.Windows.SDK.NET", StringComparison.OrdinalIgnoreCase)
            || simpleName.EndsWith(".Projection", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Describes the findings for the log, one line per referenced assembly and version, with the project assemblies
    /// that hold the reference.
    /// </summary>
    public static IReadOnlyList<string> Describe(IEnumerable<UnsatisfiedAssemblyReference> findings)
    {
        return findings
            .GroupBy(finding => (finding.Reference, finding.Required, finding.Available))
            .OrderBy(group => group.Key.Reference, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.Key.Required)
            .Select(group =>
            {
                string[] holders = group.Select(finding => finding.Assembly).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
                string shownHolders = holders.Length <= 5
                    ? string.Join(", ", holders)
                    : string.Join(", ", holders.Take(5)) + $" and {holders.Length - 5} more";
                string consequence = IsServedBySurfaceCopy(group.Key.Reference)
                    ? "the runtime does not bind a reference to an older copy, so the surface serves it with its own copy: only code that uses a type or member that the newer version adds fails, and so does the XAML metadata provider of an assembly whose type table names such a type."
                    : "the runtime does not bind a reference to an older copy, so the code of these assemblies that needs it fails, and so does the XAML metadata provider of an assembly whose type table names one of its types.";
                return $"{group.Key.Reference} {group.Key.Required} is referenced by {shownHolders}, but the design surface carries {group.Key.Available}; {consequence}";
            })
            .ToArray();
    }
}
