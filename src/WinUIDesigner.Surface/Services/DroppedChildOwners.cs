// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;

namespace WinUIDesigner.Surface.Services;

/// <summary>
/// Collects the objects the XAML reader creates for the elements marked with <c>DT.DroppedChildOwner</c>.
/// </summary>
/// <remarks>
/// The marks are set while the reader loads a document, on the UI thread, so the collection is per thread. A mark
/// that is set outside a load, for example in a template that is instantiated later, is ignored.
/// </remarks>
internal static class DroppedChildOwners
{
    [ThreadStatic]
    private static Dictionary<string, DependencyObject>? t_owners;

    /// <summary>
    /// Starts collecting marked objects on the current thread until the returned scope is disposed.
    /// </summary>
    /// <param name="owners">Receives the marked objects by the value of their mark.</param>
    public static IDisposable Collect(out IReadOnlyDictionary<string, DependencyObject> owners)
    {
        Dictionary<string, DependencyObject>? previous = t_owners;
        var current = new Dictionary<string, DependencyObject>(StringComparer.Ordinal);
        t_owners = current;
        owners = current;
        return new Scope(previous);
    }

    /// <summary>
    /// Called by <c>DT.DroppedChildOwner</c> when the mark of an object changes.
    /// </summary>
    internal static void OnMarked(DependencyObject owner, string? mark)
    {
        if (mark is not null && t_owners is { } owners)
        {
            owners[mark] = owner;
        }
    }

    private sealed class Scope(Dictionary<string, DependencyObject>? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                t_owners = previous;
            }
        }
    }
}
