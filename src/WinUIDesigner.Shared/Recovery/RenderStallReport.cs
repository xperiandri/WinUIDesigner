// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;

namespace WinUIDesigner.Recovery;

/// <summary>
/// What the surface process and the extension agree on when the XAML framework of the surface has stopped drawing. The
/// surface reports it with the unhandled-exception message, which Visual Studio already shows an error view for, and the
/// extension recognizes the report by its text: nothing in a process whose framework has stopped drawing draws again, so
/// reloading the document in the same process does not help, and the extension restarts the process. The call stack of
/// the report starts with the context of the surface (<see cref="WithContext"/>), which tells the extension whether a
/// restart can help.
/// </summary>
internal static class RenderStallReport
{
    /// <summary>
    /// The text of the report, and of the error view: what the person sees until the surface is restarted, or until a
    /// reload of the designer restarts it when it has been restarted too often.
    /// </summary>
    public const string Message = "The XAML framework reported an unspecified error and has not drawn a frame since. Reload the designer to continue.";

    /// <summary>
    /// The text of the report when the framework stopped at the first frame of the first document of the process. Every
    /// new process loads the same document and App.xaml and stops the same way, so the surface is restarted once for it,
    /// and a reload does not help.
    /// </summary>
    public const string FirstFrameMessage = "The XAML framework reported an unspecified error at the first frame of the designer and has not drawn since. The cause is in what the document or App.xaml loads, which a reload does not change: rebuild the project to start the designer again.";

    private const string _ContextPrefix = "Surface context: ";
    private static readonly char[] _LineBreaks = ['\r', '\n'];

    /// <summary>Whether the text of an unhandled-exception message is the report of a surface that has stopped drawing.</summary>
    public static bool IsReport(string? message) => Is(message, Message) || IsFirstFrameReport(message);

    /// <summary>Whether the report says that the framework stopped at the first frame of the process.</summary>
    public static bool IsFirstFrameReport(string? message) => Is(message, FirstFrameMessage);

    /// <summary>The text of the report for a framework that stopped at the first frame of the process, or later.</summary>
    public static string MessageFor(bool stoppedAtFirstFrame) => stoppedAtFirstFrame ? FirstFrameMessage : Message;

    /// <summary>The call stack of a report: the context of the surface on the first line, then the error.</summary>
    public static string WithContext(string context, string details) => $"{_ContextPrefix}{context}{Environment.NewLine}{details}";

    /// <summary>The context of the surface that a report names in its call stack; null when it names none.</summary>
    public static string? ReadContext(string? callStack)
    {
        // A call stack that does not start with a context.
        if (callStack is null || !callStack.StartsWith(_ContextPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        // The rest of the first line is the context; a string is made only for the context that is returned.
        int end = callStack.IndexOfAny(_LineBreaks, _ContextPrefix.Length);
        int length = (end < 0 ? callStack.Length : end) - _ContextPrefix.Length;
        ReadOnlySpan<char> context = callStack.AsSpan(_ContextPrefix.Length, length).Trim();

        return context.IsEmpty ? null : context.ToString();
    }

    // Visual Studio trims the message of an unhandled exception before it keeps it; the text may arrive padded.
    private static bool Is(string? message, string text) => message.AsSpan().Trim().Equals(text.AsSpan(), StringComparison.Ordinal);
}
