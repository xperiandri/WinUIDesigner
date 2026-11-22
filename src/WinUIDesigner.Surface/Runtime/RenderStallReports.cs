// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System.Collections.Generic;

namespace WinUIDesigner.Surface.Runtime;

/// <summary>
/// Remembers that the framework of the process has stopped drawing, and which surfaces have been told. Nothing in such a
/// process draws again, so every surface that is created or shown afterwards is told as well, once: otherwise Visual
/// Studio shows its document as an empty artboard with no error, also after "reload the designer", which loads the
/// document again in the same process. It has no WinUI types of its own, so that the rule can be tested.
/// </summary>
internal sealed class RenderStallReports
{
    private readonly HashSet<object> _told = new(ReferenceEqualityComparer.Instance);

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
    public bool ShouldTell(object surface) => HasStalled && _told.Add(surface);
}
