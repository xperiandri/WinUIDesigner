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
    /// Whether the method that threw the exception sits in an assembly that was loaded from the staged files of the
    /// project, and not in one of the assemblies that the surface carries or in the framework. An exception that only
    /// carries another one, such as <see cref="TargetInvocationException"/>, is looked through.
    /// </summary>
    public static bool IsThrownByProjectCode(Exception exception, string projectDirectory)
    {
        Exception thrown = Unwrap(exception);
        MethodBase? method = new StackTrace(thrown, fNeedFileInfo: false).GetFrame(0)?.GetMethod();

        return IsUnder(method?.DeclaringType?.Assembly.Location, projectDirectory);
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
