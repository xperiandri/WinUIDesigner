// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

namespace WinUIDesigner.Surface.Common;

/// <summary>
/// Decides which edits of a setter collection go through XAML Diagnostics.
/// </summary>
/// <remarks>
/// XAML Diagnostics keeps every object it is given alive until the process exits, so it gets only the edits that the
/// public API cannot apply: those of a sealed collection, whose style is in use, and those of a VisualState that can be
/// the active state, because only XAML Diagnostics updates the active state. The visual states that the actions of a
/// new document or of a lookup build are not in the live tree yet, so the public API applies their setters.
/// </remarks>
internal static class SetterEditRoute
{
    /// <param name="isSealed">Whether WinUI sealed the collection because its style is in use.</param>
    /// <param name="ownerIsVisualState">Whether the collection belongs to a VisualState rather than to a Style.</param>
    /// <param name="buildsDetachedObjects">Whether the actions build a new document or a lookup value.</param>
    public static bool UsesXamlDiagnostics(bool isSealed, bool ownerIsVisualState, bool buildsDetachedObjects)
    {
        return isSealed || (ownerIsVisualState && !buildsDetachedObjects);
    }
}
