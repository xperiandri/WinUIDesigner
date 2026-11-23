// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Text;

namespace WinUIDesigner.Surface.Runtime;

/// <summary>
/// Records the managed exceptions that are thrown on the current thread while the trace is alive, as they are thrown.
/// </summary>
/// <remarks>
/// WinUI turns an exception of managed code that it called (a <c>MeasureOverride</c> or <c>OnApplyTemplate</c> of a
/// control of the project) into an HRESULT and hands that to the caller: the failure of a layout pass reaches the
/// surface as "Catastrophic failure (0x8000FFFF)" with no hint of what threw. The first-chance notification comes
/// before that, with the exception itself and the stack at the place where it was thrown.
/// </remarks>
internal sealed class FirstChanceExceptionTrace : IDisposable
{
    private const int _Frames = 6;

    private readonly int _threadId = Environment.CurrentManagedThreadId;
    private readonly int _capacity;
    private readonly List<string> _exceptions = [];

    /// <summary>
    /// Starts to record.
    /// </summary>
    /// <param name="capacity">The number of exceptions to keep; the others are counted nowhere and dropped.</param>
    public FirstChanceExceptionTrace(int capacity = 20)
    {
        _capacity = capacity;
        AppDomain.CurrentDomain.FirstChanceException += OnFirstChanceException;
    }

    /// <summary>
    /// Gets the exceptions recorded so far, one line each, in the order they were thrown.
    /// </summary>
    public IReadOnlyList<string> Exceptions
    {
        get
        {
            lock (_exceptions)
            {
                return [.. _exceptions];
            }
        }
    }

    /// <summary>
    /// Stops recording.
    /// </summary>
    public void Dispose() => AppDomain.CurrentDomain.FirstChanceException -= OnFirstChanceException;

    private void OnFirstChanceException(object? sender, FirstChanceExceptionEventArgs e)
    {
        // Another thread throws for its own reasons.
        if (Environment.CurrentManagedThreadId != _threadId)
        {
            return;
        }

        try
        {
            string line = Describe(e.Exception, new StackTrace(skipFrames: 2, fNeedFileInfo: false));
            lock (_exceptions)
            {
                if (_exceptions.Count < _capacity)
                {
                    _exceptions.Add(line);
                }
            }
        }
        catch
        {
            // A trace must not throw in the place where something else threw.
        }
    }

    /// <summary>
    /// Describes an exception in one line: its type, HResult and first line of the message, and the methods at the place
    /// where it was thrown.
    /// </summary>
    internal static string Describe(Exception exception, StackTrace stack)
    {
        string message = exception.Message ?? string.Empty;
        int lineEnd = message.IndexOfAny(['\r', '\n']);
        if (lineEnd >= 0)
        {
            message = message[..lineEnd];
        }

        var text = new StringBuilder();
        text.Append($"{exception.GetType().FullName} (0x{exception.HResult:X8}): {message}");

        StackFrame[] frames = stack.GetFrames();
        for (int index = 0; index < frames.Length && index < _Frames; index++)
        {
            var method = frames[index].GetMethod();
            text.Append(index == 0 ? " at " : " < ").Append($"{method?.DeclaringType?.FullName}.{method?.Name}");
        }

        return text.ToString();
    }
}
