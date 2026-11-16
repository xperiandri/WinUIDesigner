// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;
using System.Linq;

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
/// Finds the references of the project's assemblies that cannot be bound in the surface process.
/// </summary>
/// <remarks>
/// The surface carries its own copies of many assemblies, such as <c>Microsoft.Windows.SDK.NET</c>, the projection
/// of the Windows SDK. The runtime binds a reference to an assembly of that name to the surface's copy, and only
/// when that copy is not older than the version that was referenced. A project that was compiled against a newer
/// version fails to load with <c>0x80131040</c> ("the located assembly's manifest definition does not match the
/// assembly reference"), and when that happens inside a callback of the XAML framework, the surface process ends.
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
                return $"{group.Key.Reference} {group.Key.Required} is referenced by {shownHolders}, but the design surface carries {group.Key.Available}; "
                    + "the runtime binds the reference to the surface's copy, which is older, so these assemblies cannot be loaded.";
            })
            .ToArray();
    }
}
