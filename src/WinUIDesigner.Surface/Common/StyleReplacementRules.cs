// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;

namespace WinUIDesigner.Surface.Common;

/// <summary>
/// Decides which references to a rebuilt style move to it, and where the surface looks for them.
/// </summary>
/// <remarks>
/// Visual Studio rebuilds a style, for example after its TargetType changed, and the rebuilt style can target another
/// type than the style it replaces. WinUI rejects a style that breaks its TargetType rules, so a reference that cannot
/// take the rebuilt style keeps the old one instead of failing the edit.
/// </remarks>
internal static class StyleReplacementRules
{
    /// <summary>
    /// Returns whether <paramref name="element"/> can use a style with <paramref name="targetType"/>: the element has to
    /// be an instance of that type.
    /// </summary>
    public static bool CanStyle(Type? targetType, object element) => targetType is null || targetType.IsInstanceOfType(element);

    /// <summary>
    /// Returns whether a style with <paramref name="targetType"/> can be BasedOn a style with
    /// <paramref name="baseTargetType"/>: the base has to target the same type or one of its base types.
    /// </summary>
    public static bool CanBaseOn(Type? targetType, Type? baseTargetType)
    {
        return targetType is null || baseTargetType is null || baseTargetType.IsAssignableFrom(targetType);
    }

    /// <summary>
    /// Returns whether the surface looks in a resource dictionary for the styles BasedOn a rebuilt style.
    /// </summary>
    /// <remarks>
    /// Enumerating a dictionary creates its deferred resources. The framework's control styles and the themes of
    /// libraries hold thousands; they load from a Source, like every dictionary of another file, and the XAML reader
    /// reads such a file on its own, so its styles cannot be BasedOn a style of the document being edited.
    /// </remarks>
    /// <param name="isFrameworkResources">Whether the dictionary holds the framework's control styles.</param>
    /// <param name="source">The file the dictionary loaded from, if any.</param>
    public static bool SearchesDictionary(bool isFrameworkResources, Uri? source) => !isFrameworkResources && source is null;
}
