// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace WinUIDesigner.Surface.Runtime;

/// <summary>
/// Finds the construction actions that name the same object handles as an action that failed.
/// </summary>
/// <remarks>
/// An action fails on a handle that an earlier action filled: the object is missing, it is <see langword="null"/>, or it
/// is not what the failed action expects. The log of the failure then shows the earlier actions too, so that it tells where
/// the object came from and not only where it was used.
/// </remarks>
internal static partial class FailedActionContext
{
    /// <summary>
    /// Finds the latest actions before <paramref name="failedIndex"/> that name a handle of the failed action.
    /// </summary>
    /// <returns>The index and the serialized form of up to <paramref name="maxCount"/> actions, the oldest first.</returns>
    public static IReadOnlyList<(int Index, string Action)> FindRelated(IReadOnlyList<string> actions, int failedIndex, int maxCount)
    {
        // There is no such action.
        if (failedIndex < 0 || failedIndex >= actions.Count)
        {
            return [];
        }

        // The failed action names no object, only values.
        HashSet<string> handles = ReadHandles(actions[failedIndex]);
        if (handles.Count == 0)
        {
            return [];
        }

        // The latest action that names a handle explains its state best, so the search goes backwards.
        var related = new List<(int Index, string Action)>();
        for (int index = failedIndex - 1; index >= 0 && related.Count < maxCount; index--)
        {
            if (ReadHandles(actions[index]).Overlaps(handles))
            {
                related.Add((index, actions[index]));
            }
        }

        related.Reverse();
        return related;
    }

    // "ParentHandle":-63 and the like: a number in a property whose name ends with Handle.
    [GeneratedRegex("\"\\w*Handle\":(?<handle>-?\\d+)")]
    private static partial Regex HandleProperty();

    private static HashSet<string> ReadHandles(string action)
    {
        var handles = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in HandleProperty().Matches(action))
        {
            string handle = match.Groups["handle"].Value;

            // Zero is no object: the owner of an action that has none.
            if (handle != "0")
            {
                handles.Add(handle);
            }
        }

        return handles;
    }
}
