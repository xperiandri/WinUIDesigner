// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace WinUIDesigner.Surface.Runtime;

/// <summary>
/// Finds a resource of the project that the surface's own resource manager does not know among what Visual Studio
/// staged in the content directory. MRT asks for <c>ms-appx:///Assets/Images/logo.svg</c> by the name
/// <c>Files/Assets/Images/logo.svg</c>; a packaged application finds such a file in its package, the surface has none.
/// </summary>
internal static class StagedResourceLookup
{
    private const string _FilesPrefix = "Files/";

    // The PRI file of a packaged application. It lists the files of the application and of the libraries it references.
    private const string _ApplicationPriFileName = "resources.pri";

    // The loose layout of an older packaged deployment, which the build output keeps: its files are stale.
    private const string _AppXFolderName = "AppX";

    private static readonly char[] _InvalidFileNameCharacters = Path.GetInvalidFileNameChars();

    /// <summary>
    /// Whether <paramref name="name"/> names a file of the application, as <c>Files/Assets/Images/logo.svg</c> does.
    /// </summary>
    public static bool IsFileResource(string name) => name.StartsWith(_FilesPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The staged PRI files in the order in which they are asked. The files at the root of the content directory come
    /// first: a PRI file names its files by paths below its own folder, and only at the root are these the staged files.
    /// Among the files of one folder depth, the PRI file of the application comes first, because it answers nearly
    /// every name.
    /// </summary>
    public static List<string> OrderPriFiles(string root, IEnumerable<string> files)
    {
        return
        [
            .. files
                .OrderBy(file => Path.GetRelativePath(root, file).AsSpan().Count(Path.DirectorySeparatorChar))
                .ThenBy(file => Path.GetFileName(file.AsSpan()).Equals(_ApplicationPriFileName, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(file => file, StringComparer.OrdinalIgnoreCase),
        ];
    }

    /// <summary>
    /// The path below <paramref name="root"/> that the file resource <paramref name="name"/> names, or
    /// <see langword="null"/> for a name that is no file resource or that would lead out of <paramref name="root"/>.
    /// The name comes from the XAML of the project, so nothing in it may reach another folder.
    /// </summary>
    public static string? GetStagedPath(string root, string name)
    {
        if (!IsFileResource(name))
        {
            return null;
        }

        // The first segment is "Files".
        string[] segments = name.Split('/');
        for (int index = 1; index < segments.Length; index++)
        {
            string segment = Uri.UnescapeDataString(segments[index]);

            // An empty segment, the current or the parent folder, a rooted segment ("C:") and a segment with a
            // character that no file name has (an escaped '/' or '\' among them) would not name the file of the URI.
            if (segment is "" or "." or ".." || Path.IsPathRooted(segment) || segment.IndexOfAny(_InvalidFileNameCharacters) >= 0)
            {
                return null;
            }

            segments[index] = segment;
        }

        // The AppX folder of the content directory is never used.
        if (segments[1].Equals(_AppXFolderName, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string fullRoot = Path.GetFullPath(root);
        string path = Path.GetFullPath(Path.Combine([fullRoot, .. segments.AsSpan(1)]));
        string prefix = Path.EndsInDirectorySeparator(fullRoot) ? fullRoot : $"{fullRoot}{Path.DirectorySeparatorChar}";

        // Whatever the segments are, the file is below the root.
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? path : null;
    }

    /// <summary>
    /// The staged file that the file resource <paramref name="name"/> names, when it exists: the file at the exact
    /// path of the URI below <paramref name="root"/>.
    /// </summary>
    public static string? FindStagedFile(string root, string name, Func<string, bool> fileExists)
    {
        return GetStagedPath(root, name) is { } path && fileExists(path) ? path : null;
    }

    /// <summary>
    /// The URI by which the XAML of the project refers to the file resource <paramref name="name"/>, for messages.
    /// </summary>
    public static string ToAppUri(string name) => IsFileResource(name) ? $"ms-appx:///{name.AsSpan(_FilesPrefix.Length)}" : name;

    /// <summary>
    /// The qualifier values that are set, ordered by name, such as <c>Scale=150, Theme=Dark</c>, for messages.
    /// </summary>
    public static string DescribeQualifiers(IEnumerable<KeyValuePair<string, string>> qualifiers)
    {
        return string.Join(
            ", ",
            qualifiers
                .Where(qualifier => !string.IsNullOrEmpty(qualifier.Value))
                .OrderBy(qualifier => qualifier.Key, StringComparer.Ordinal)
                .Select(qualifier => $"{qualifier.Key}={qualifier.Value}")
        );
    }
}
