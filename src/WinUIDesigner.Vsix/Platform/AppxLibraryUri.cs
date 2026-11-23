// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;

namespace WinUIDesigner.Platform;

/// <summary>
/// Reads a source that names a file of a project by the assembly name of that project, such as
/// <c>ms-appx:///Ecierge.Uno.Controls/Styles.xaml</c>.
/// </summary>
/// <remarks>
/// At run time the first segment of such a path is the folder that the package of a library has next to the
/// application's own files, and it is the assembly name of the library. Visual Studio tells that segment from a folder
/// of the project by looking for a project with that assembly name among the projects the application references.
/// </remarks>
internal static class AppxLibraryUri
{
    private const string _AppxScheme = "ms-appx";
    private const string _DesignTimeMarker = "~vs]~";

    /// <summary>
    /// Splits a source into the assembly name of the project that holds the file and the path of the file in that
    /// project.
    /// </summary>
    /// <param name="value">The source, as Visual Studio passes it to its markup provider.</param>
    /// <param name="assemblyName">The first segment of the path: the assembly name of the project.</param>
    /// <param name="projectRelativePath">
    /// The rest of the path, in the form Visual Studio looks files up in a project: with a leading backslash and
    /// backslashes between the folders.
    /// </param>
    /// <returns>
    /// <see langword="true"/> for an absolute <c>ms-appx</c> URI without a host, and for a path that starts with a
    /// slash (relative to the root of the package, as the XAML reader reads it), when they name a file below a folder.
    /// A path without a leading slash is relative to the document that names it and names no assembly; a URI with a
    /// host names a package, which Visual Studio resolves itself; a copy that Visual Studio prepared is found by its
    /// id; and a file in the root of the package belongs to the application.
    /// </returns>
    public static bool TrySplit(string? value, out string assemblyName, out string projectRelativePath)
    {
        assemblyName = string.Empty;
        projectRelativePath = string.Empty;
        // An empty value names nothing.
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string text = value!.Trim();
        string path;
        if (text.StartsWith("/", StringComparison.Ordinal) && !text.StartsWith("//", StringComparison.Ordinal))
        {
            // Rooted: relative to the root of the package.
            path = text;
        }
        else if (Uri.TryCreate(text, UriKind.Absolute, out Uri? uri)
            && string.Equals(uri.Scheme, _AppxScheme, StringComparison.OrdinalIgnoreCase)
            && uri.Host.Length == 0)
        {
            path = uri.AbsolutePath;
        }
        else
        {
            // Relative to the document, or the URI of another package: not a path of the package.
            return false;
        }

        // A copy that Visual Studio prepared is found by its id.
        path = Uri.UnescapeDataString(path).TrimStart('/');
        if (path.IndexOf(_DesignTimeMarker, StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return false;
        }

        // "Assembly/Folder/File.xaml": a folder of the package, and a file below it.
        // A file in the root of the package belongs to the application, and a folder is no file.
        int slash = path.IndexOf('/');
        if (slash <= 0 || slash == path.Length - 1)
        {
            return false;
        }

        assemblyName = path.Substring(0, slash);
        projectRelativePath = $"\\{path.Substring(slash + 1).Replace('/', '\\')}";
        return true;
    }
}
