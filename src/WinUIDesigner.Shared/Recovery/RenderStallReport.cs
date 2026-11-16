// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;

namespace WinUIDesigner.Recovery;

/// <summary>
/// What the surface process and the extension agree on when the XAML framework of the surface has stopped drawing. The
/// surface reports it with the unhandled-exception message, which Visual Studio already shows an error view for, and the
/// extension recognizes the report by its text: nothing in a process whose framework has stopped drawing draws again, so
/// reloading the document in the same process does not help, and the extension restarts the process.
/// </summary>
internal static class RenderStallReport
{
    /// <summary>
    /// The text of the report, and of the error view: what the person sees until the surface is restarted, or for good
    /// when it has been restarted too often.
    /// </summary>
    public const string Message = "The XAML framework reported an unspecified error and has not drawn a frame since. Reload the designer to continue.";

    /// <summary>Whether the text of an unhandled-exception message is the report of a surface that has stopped drawing.</summary>
    public static bool IsReport(string? message) => string.Equals(message?.Trim(), Message, StringComparison.Ordinal);
}
