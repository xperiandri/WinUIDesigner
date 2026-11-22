// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.IO;

namespace WinUIDesigner.Surface.Runtime;

/// <summary>
/// Describes the failures to load an assembly or a type for the surface's log.
/// </summary>
/// <remarks>
/// The message of such an exception is often empty in the single-file surface, so a log line names the assembly or
/// the type, the exception and its HRESULT, which tell the cause: <c>0x80131040</c>, for example, is "the located
/// assembly's manifest definition does not match the assembly reference".
/// </remarks>
internal static class AssemblyLoadFailures
{
    /// <summary>
    /// Returns whether an exception is a failure to load an assembly or a type that is worth writing to the log.
    /// </summary>
    public static bool IsWorthLogging(Exception exception)
    {
        return exception switch
        {
            // A satellite assembly of resources that is not there: the runtime probes for them all the time.
            FileNotFoundException { FileName: { } fileName } when IsSatelliteAssembly(fileName) => false,
            // An assembly that is not there, or one that is there but cannot be loaded.
            FileNotFoundException or FileLoadException or BadImageFormatException => true,
            // A type that cannot be loaded.
            TypeLoadException => true,
            _ => false,
        };
    }

    /// <summary>
    /// Returns whether the name of an assembly that could not be found is the name of a satellite assembly of
    /// resources. Only the simple name of a satellite ends with <c>.resources</c>, and a satellite never has the
    /// neutral culture, which an ordinary assembly whose name ends the same way, such as <c>Contoso.Resources</c>, has.
    /// </summary>
    /// <param name="fileName">
    /// The display name of the assembly, such as <c>Contoso.resources, Version=1.0.0.0, Culture=de-DE, PublicKeyToken=null</c>,
    /// or a file name.
    /// </param>
    public static bool IsSatelliteAssembly(string fileName)
    {
        // The simple name, before the first comma of a display name.
        ReadOnlySpan<char> rest = fileName;
        int comma = rest.IndexOf(',');
        if (!(comma < 0 ? rest : rest[..comma]).Trim().EndsWith(".resources", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // The culture, when the display name gives one.
        while (comma >= 0)
        {
            rest = rest[(comma + 1)..];
            comma = rest.IndexOf(',');
            ReadOnlySpan<char> part = comma < 0 ? rest : rest[..comma];
            int equals = part.IndexOf('=');
            if (equals > 0 && part[..equals].Trim().Equals("Culture", StringComparison.OrdinalIgnoreCase))
            {
                return !part[(equals + 1)..].Trim().Equals("neutral", StringComparison.OrdinalIgnoreCase);
            }
        }

        // A name without a culture.
        return true;
    }

    /// <summary>
    /// Describes a failure to load an assembly or a type for the log: the exception, its HRESULT, the assembly or the
    /// type it names, and its message when it has one.
    /// </summary>
    public static string Describe(Exception exception)
    {
        string description = GetSubject(exception) is { } subject
            ? $"{exception.GetType().Name} 0x{exception.HResult:X8} while loading '{subject}'"
            : $"{exception.GetType().Name} 0x{exception.HResult:X8}";

        return string.IsNullOrEmpty(exception.Message) ? description : $"{description}: {exception.Message}";
    }

    // The assembly, by its display name or its path, or the type that could not be loaded, when the exception names it.
    private static string? GetSubject(Exception exception)
    {
        return exception switch
        {
            FileNotFoundException { FileName: { Length: > 0 } fileName } => fileName,
            FileLoadException { FileName: { Length: > 0 } fileName } => fileName,
            BadImageFormatException { FileName: { Length: > 0 } fileName } => fileName,
            TypeLoadException { TypeName: { Length: > 0 } typeName } => typeName,
            _ => null,
        };
    }
}
