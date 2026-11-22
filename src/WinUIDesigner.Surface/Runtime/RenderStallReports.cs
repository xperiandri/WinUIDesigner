// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System.Runtime.CompilerServices;

namespace WinUIDesigner.Surface.Runtime;

/// <summary>
/// Remembers that the framework of the process has stopped drawing, and which surfaces have been told. Nothing in such a
/// process draws again, so every surface that is created or shown afterwards is told as well, once: otherwise Visual
/// Studio shows its document as an empty artboard with no error, also after "reload the designer", which loads the
/// document again in the same process. It has no WinUI types of its own, so that the rule can be tested.
/// </summary>
internal sealed class RenderStallReports
{
    private static readonly object _Told = new();

    // The surfaces are held weakly: a process whose framework has stopped can go on for a long time, and a surface that
    // was closed must not be kept alive, with its whole document, only because it was told.
    private readonly ConditionalWeakTable<object, object> _told = new();

    /// <summary>Whether the framework of the process has stopped drawing.</summary>
    public bool HasStalled { get; private set; }

    /// <summary>The text of the report, which the extension recognizes.</summary>
    public string Message { get; private set; } = string.Empty;

    /// <summary>The error after which the framework stopped drawing.</summary>
    public string Details { get; private set; } = string.Empty;

    public void Stalled(string message, string details)
    {
        HasStalled = true;
        Message = message;
        Details = details;
    }

    /// <summary>Whether the surface still has to be told that nothing is drawn: once for each surface, after the stall.</summary>
    public bool ShouldTell(object surface) => HasStalled && _told.TryAdd(surface, _Told);
}
