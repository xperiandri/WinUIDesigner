// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.InstanceBuilders.Shared;

namespace WinUIDesigner.Surface.Common;

/// <summary>A <c>{ThemeResource}</c> that the surface evaluated while it executed an action.</summary>
/// <param name="Owner">The object that the reference was evaluated at.</param>
/// <param name="Key">The resource key of the reference.</param>
/// <param name="Theme">The theme that it was evaluated in.</param>
internal readonly record struct ThemeResourceUse(object Owner, object Key, string Theme);

/// <summary>
/// Keeps a <see cref="ThemeResourceRegistry"/> in step with the construction actions that the surface executes. WinUI-free:
/// the owner of an action and the document of an owner come through functions.
/// </summary>
internal static class ThemeResourceTracking
{
    /// <summary>
    /// Follows an executed action: a property that <see cref="SetPropertyAction"/> set from the reference in
    /// <paramref name="evaluated"/> is remembered while its owner belongs to a document, and a property that an action set
    /// to anything else or <see cref="ClearPropertyAction"/> cleared is forgotten. Other actions change nothing.
    /// </summary>
    /// <param name="registry">The properties that are remembered.</param>
    /// <param name="action">The action that was executed.</param>
    /// <param name="evaluated">The reference that executing the action evaluated, if any.</param>
    /// <param name="resolveOwner">The object that a handle of an action stands for; <see langword="null"/> when none does.</param>
    /// <param name="documentOf">The id of the document that an object belongs to; 0 when it belongs to none.</param>
    /// <returns>
    /// <see langword="true"/> when the action set or cleared a <c>RequestedTheme</c>, which moves the theme of the
    /// references below its owner: the caller evaluates them again at the end of the batch.
    /// </returns>
    public static bool Track(
        ThemeResourceRegistry registry,
        XamlAction action,
        ThemeResourceUse? evaluated,
        Func<long, object?> resolveOwner,
        Func<object, int> documentOf)
    {
        (long ownerHandle, string property) = action switch
        {
            SetPropertyAction set => (set.ParentHandle, set.FullPropertyName),
            ClearPropertyAction clear => (clear.ParentHandle, clear.FullPropertyName),
            _ => (0L, string.Empty),
        };

        // Not a property action, or an owner that no object stands for.
        if (ownerHandle == 0 || resolveOwner(ownerHandle) is not { } owner)
        {
            return false;
        }

        // Set from a reference that was evaluated at this owner, which belongs to a document that releases it later.
        if (action is SetPropertyAction && evaluated is { } use && ReferenceEquals(use.Owner, owner) && documentOf(owner) != 0)
        {
            registry.Record(owner, property, use.Key, use.Theme);
        }
        else
        {
            // Set to something else, or cleared: an earlier reference no longer decides the value.
            registry.Forget(owner, property);
        }

        return ThemeResourceThemes.IsRequestedThemeProperty(property);
    }
}
