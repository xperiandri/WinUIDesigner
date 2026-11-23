// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;
using System.Linq;

namespace WinUIDesigner.Platform;

/// <summary>
/// Finds a project by its assembly name among the projects that a project and its application reference.
/// </summary>
/// <remarks>
/// Visual Studio looks for the assembly named in a <c>ms-appx:///Assembly/...</c> source only among the projects that
/// the application references directly. A library that the application reaches through another library is not
/// among them, although the library that references it names it with the same path, and the dictionary of the first
/// library is not found.
/// </remarks>
internal static class TransitiveProjectLookup
{
    /// <summary>
    /// Finds the project whose assembly is named <paramref name="assemblyName"/>.
    /// </summary>
    /// <typeparam name="T">The type of a project.</typeparam>
    /// <param name="owner">The project of the document that names the source.</param>
    /// <param name="application">The application that <paramref name="owner"/> belongs to; <see langword="null"/> if there is none.</param>
    /// <param name="referencesOf">Lists the projects that a project references directly.</param>
    /// <param name="assemblyNameOf">Returns the assembly name of a project.</param>
    /// <param name="identityOf">Returns what tells two projects apart, such as the GUID of the project.</param>
    /// <param name="assemblyName">The assembly name, which is compared without regard to case.</param>
    /// <returns>
    /// The project, or <see langword="null"/> when there is none. The references of <paramref name="owner"/> come first:
    /// a library names its own dependencies by assembly name. Then comes everything that the application reaches,
    /// breadth first, so that the project nearest to the application wins when several have the name. A project that
    /// is reached again ends that path, so a cycle of references ends the search.
    /// </returns>
    public static ProjectLookupResult<T>? Find<T>(
        T owner,
        T? application,
        Func<T, IEnumerable<T>?> referencesOf,
        Func<T, string?> assemblyNameOf,
        Func<T, object> identityOf,
        string assemblyName)
        where T : class
    {
        bool Matches(T project) => string.Equals(assemblyNameOf(project), assemblyName, StringComparison.OrdinalIgnoreCase);

        // The owner's own references.
        foreach (T reference in referencesOf(owner) ?? Enumerable.Empty<T>())
        {
            if (Matches(reference))
            {
                return new ProjectLookupResult<T>(new[] { owner, reference });
            }
        }

        // Everything the application reaches, nearest first.
        if (application is null)
        {
            return null;
        }

        var visited = new HashSet<object> { identityOf(application) };
        var pending = new Queue<IReadOnlyList<T>>();
        pending.Enqueue(new[] { application });
        while (pending.Count != 0)
        {
            IReadOnlyList<T> path = pending.Dequeue();
            foreach (T reference in referencesOf(path[path.Count - 1]) ?? Enumerable.Empty<T>())
            {
                // Reached before, by a path that is not longer: a cycle of references ends here.
                if (!visited.Add(identityOf(reference)))
                {
                    continue;
                }

                // The nearest project with the name wins.
                T[] referencePath = path.Concat(new[] { reference }).ToArray();
                if (Matches(reference))
                {
                    return new ProjectLookupResult<T>(referencePath);
                }

                pending.Enqueue(referencePath);
            }
        }

        return null;
    }
}

/// <summary>
/// The project that a lookup found and the chain of references that leads to it.
/// </summary>
/// <typeparam name="T">The type of a project.</typeparam>
/// <param name="path">The project the search started from, the projects it went through, and the project found.</param>
internal sealed class ProjectLookupResult<T>(IReadOnlyList<T> path)
{
    /// <summary>
    /// Gets the project that was found.
    /// </summary>
    public T Project => path[path.Count - 1];

    /// <summary>
    /// Gets the project the search started from, the projects it went through, and the project found.
    /// </summary>
    public IReadOnlyList<T> Path => path;

    /// <summary>
    /// Describes how the project is reached, for a log: <c>'Console.UI' references it</c> when the project the
    /// search started from references it directly, and <c>'Console.App' references it through 'Console.UI'</c> when
    /// there are projects in between.
    /// </summary>
    /// <param name="nameOf">Returns the name of a project.</param>
    public string DescribeRoute(Func<T, string> nameOf)
    {
        string start = nameOf(path[0]);
        if (path.Count <= 2)
        {
            return $"'{start}' references it";
        }

        string through = string.Join(", ", path.Skip(1).Take(path.Count - 2).Select(nameOf).Select(name => $"'{name}'"));
        return $"'{start}' references it through {through}";
    }
}
