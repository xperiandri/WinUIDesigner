// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;

namespace WinUIDesigner.Surface.Runtime;

/// <summary>
/// The latest events of the surface, a fixed number of them, to put into the log when something fails and the log
/// would otherwise not say what the surface had been doing. Events can be added from any thread.
/// </summary>
internal sealed class Breadcrumbs
{
    private readonly string[] _entries;
    private readonly object _gate = new();
    private int _next;
    private int _count;

    public Breadcrumbs(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);

        _entries = new string[capacity];
    }

    public void Add(string text)
    {
        // The time separator of a culture can be a dot; the log reads the same on every machine.
        string entry = FormattableString.Invariant($"{DateTime.Now:HH:mm:ss.fff} {text}");

        lock (_gate)
        {
            _entries[_next] = entry;
            _next = (_next + 1) % _entries.Length;
            _count = Math.Min(_count + 1, _entries.Length);
        }
    }

    /// <summary>The entries, the oldest first.</summary>
    public IReadOnlyList<string> Snapshot()
    {
        lock (_gate)
        {
            var result = new List<string>(_count);
            int first = (_next - _count + _entries.Length) % _entries.Length;
            for (int index = 0; index < _count; index++)
            {
                result.Add(_entries[(first + index) % _entries.Length]);
            }

            return result;
        }
    }
}
