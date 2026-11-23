// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System.Collections.Generic;

namespace WinUIDesigner.Markup;

/// <summary>
/// Remembers the text of each document that something was last reported for, so that the same text is reported
/// once however often it is looked at again.
/// </summary>
/// <remarks>
/// A designer looks at the same documents on every request, and most of them have not changed since the last one.
/// A text counts as the same while its version and its length are: not every text buffer counts versions
/// (<c>SimpleTextBuffer</c> always reports 0), so the length takes part in telling one text from the next.
/// Only the latest text of a document is kept. The type is not safe to use from several threads at once.
/// </remarks>
internal sealed class ReportedTextVersions
{
    private readonly Dictionary<int, (int Version, int Length)> _reported = [];

    /// <summary>
    /// Records that <paramref name="documentId"/> is reported in the text it has now.
    /// </summary>
    /// <param name="documentId">The identity of the document.</param>
    /// <param name="textVersion">The version of the text buffer.</param>
    /// <param name="textLength">The length of the text.</param>
    /// <returns>
    /// <see langword="true"/> when this text of the document was not recorded yet, so it is reported now;
    /// <see langword="false"/> when it was the text that the document was last reported in.
    /// </returns>
    public bool TryRecord(int documentId, int textVersion, int textLength)
    {
        // The document was reported in this very text.
        (int Version, int Length) text = (textVersion, textLength);
        if (_reported.TryGetValue(documentId, out (int Version, int Length) last) && last == text)
        {
            return false;
        }

        _reported[documentId] = text;
        return true;
    }
}
