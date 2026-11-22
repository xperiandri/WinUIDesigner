// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace WinUIDesigner.Surface.Runtime;

/// <summary>
/// Tells the exceptions that code of the previewed project threw from the exceptions of the surface and of the
/// framework. It has no WinUI types of its own, so that the rule can be tested.
/// </summary>
internal static class ProjectCodeExceptions
{
    /// <summary>
    /// Whether the exception is a failure of code of the project: the first method on its stack that is not the
    /// runtime's or the framework's sits in an assembly that was loaded from the staged files of the project. A method of
    /// the framework that project code called, such as a guard or a parser that throws, counts as the project's; a method
    /// of the surface (<paramref name="surface"/>, the assembly of the surface by default) makes the failure the
    /// surface's. An exception that only carries another one, such as <see cref="TargetInvocationException"/>, is looked
    /// through.
    /// </summary>
    public static bool IsThrownByProjectCode(Exception exception, string projectDirectory, Assembly? surface = null)
    {
        surface ??= typeof(ProjectCodeExceptions).Assembly;
        foreach (StackFrame frame in new StackTrace(Unwrap(exception), fNeedFileInfo: false).GetFrames())
        {
            // A method that was made at run time has no file that tells where it comes from.
            Assembly? assembly = frame.GetMethod()?.DeclaringType?.Assembly;
            if (assembly is null || assembly.IsDynamic)
            {
                continue;
            }

            // Code of the project, or of a library that it staged.
            if (IsUnder(assembly.Location, projectDirectory))
            {
                return true;
            }

            // Code of the surface: the failure is the surface's, whoever called it.
            if (assembly == surface)
            {
                return false;
            }

            // The runtime, the framework, a projection or Visual Studio: the method that called it decides.
        }

        return false;
    }

    /// <summary>
    /// Whether the error came from outside of managed code: the framework reports a failure of its own, such as
    /// <c>E_FAIL</c> while it lays out or draws an element, as an exception that no managed method threw, so that it has
    /// no stack. An exception that managed code threw, or that was thrown and caught on the way, has one.
    /// </summary>
    public static bool IsRaisedByNativeCode(Exception exception)
    {
        return new StackTrace(Unwrap(exception), fNeedFileInfo: false).FrameCount == 0;
    }

    /// <summary>
    /// Whether the file is in the directory or in a directory below it. An assembly that is part of a single file has no
    /// location, and is not in any directory.
    /// </summary>
    public static bool IsUnder(string? file, string directory)
    {
        if (string.IsNullOrEmpty(file) || string.IsNullOrEmpty(directory))
        {
            return false;
        }

        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) + Path.DirectorySeparatorChar;

        return Path.GetFullPath(file).StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static Exception Unwrap(Exception exception)
    {
        while (exception.InnerException is { } inner
            && exception is TargetInvocationException or TypeInitializationException or AggregateException { InnerExceptions.Count: 1 })
        {
            exception = inner;
        }

        return exception;
    }
}
