// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;
using System.IO;

namespace WinUIDesigner.Surface.Runtime;

/// <summary>
/// The assemblies the surface carries itself, by simple name: the trusted platform assemblies of its process.
/// </summary>
/// <remarks>
/// The runtime binds a reference to one of these names only to the surface's copy. Simple names are compared without
/// case, as the runtime compares them, so a reference whose casing differs from the file name still finds the copy.
/// </remarks>
internal sealed class SurfaceAssemblies
{
    private readonly Dictionary<string, string> _paths = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, Version?> _versions = new(StringComparer.OrdinalIgnoreCase);

    private readonly Func<string, Version?> _readVersion;

    /// <param name="trustedPlatformAssemblies">
    /// The paths of the assemblies, separated by <see cref="Path.PathSeparator"/>, as the runtime gives them in
    /// <c>TRUSTED_PLATFORM_ASSEMBLIES</c>, or <see langword="null"/> when it gives none.
    /// </param>
    /// <param name="readVersion">
    /// Reads the version of the assembly at a path, or returns <see langword="null"/> when it cannot be read. A reader
    /// that throws is taken to have read nothing (<see cref="GetVersion"/>).
    /// </param>
    public SurfaceAssemblies(string? trustedPlatformAssemblies, Func<string, Version?> readVersion)
    {
        _readVersion = readVersion;
        foreach (string path in (trustedPlatformAssemblies ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            _paths.TryAdd(Path.GetFileNameWithoutExtension(path), path);
        }
    }

    /// <summary>
    /// Returns whether the surface carries an assembly of a simple name.
    /// </summary>
    public bool Contains(string simpleName) => _paths.ContainsKey(simpleName);

    /// <summary>
    /// Gets the version of the assembly the surface carries under a simple name, or <see langword="null"/> when it
    /// carries none or its version cannot be read. It does not throw: the handlers of the runtime that ask for it must
    /// not fail because a file of the surface is unreadable.
    /// </summary>
    public Version? GetVersion(string simpleName)
    {
        lock (_versions)
        {
            // The first request for a name reads the version of the surface's copy; a name it does not carry has none.
            if (!_versions.TryGetValue(simpleName, out Version? version))
            {
                version = _paths.TryGetValue(simpleName, out string? path) ? ReadVersion(path) : null;
                _versions[simpleName] = version;
            }

            return version;
        }
    }

    private Version? ReadVersion(string path)
    {
        try
        {
            return _readVersion(path);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A file that is not readable, such as one the host denies access to, has no version to compare, as one that
            // is not an assembly has none. It is not read again: the answer is remembered like any other.
            return null;
        }
    }
}
