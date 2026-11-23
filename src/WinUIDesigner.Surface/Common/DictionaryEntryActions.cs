// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using XSurfUwp;

namespace WinUIDesigner.Surface.Common;

/// <summary>
/// Reaches into the live dictionaries that Visual Studio's dictionary actions address.
/// </summary>
internal interface IDictionaryEntryAccess
{
    /// <summary>
    /// Returns the entry of <paramref name="dictionary"/> with <paramref name="key"/>, or <see langword="null"/>.
    /// </summary>
    object? GetEntry(object dictionary, object? key);

    /// <summary>
    /// Adds an entry to <paramref name="dictionary"/>.
    /// </summary>
    void AddEntry(object dictionary, object? key, object? value);

    /// <summary>
    /// Replaces the value of the entry of <paramref name="dictionary"/> with <paramref name="key"/>.
    /// </summary>
    void ReplaceEntry(object dictionary, object? key, object? value);

    /// <summary>
    /// Removes the entry of <paramref name="dictionary"/> with <paramref name="key"/>.
    /// </summary>
    void RemoveEntry(object dictionary, object? key);
}

/// <summary>
/// Applies Visual Studio's dictionary actions to live dictionaries, and ends them at the stand-in of a dropped dictionary.
/// </summary>
/// <remarks>
/// <para>
/// Visual Studio builds almost nothing beneath a dropped element: the exception is the entries of a dropped merged
/// dictionary. Its dictionary-entry builder reaches an entry through the proxy of the dictionary, which stays valid, so
/// a change of the entry sends GetDictionaryEntry, UpdateDictionaryValue, AddDictionaryEntry and RemoveDictionaryEntry
/// for the dictionary, and the actions that follow address the entry that the lookup gave.
/// </para>
/// <para>
/// Nothing lives in place of a dropped dictionary. The dictionary is the stand-in that the surface hands out at its
/// position in the collection of merged dictionaries: a lookup in it gives the stand-in again, so that the actions that
/// follow reach the guards of the stand-in, and every change of it changes nothing.
/// </para>
/// </remarks>
/// <param name="dictionaries">Reaches into the live dictionaries.</param>
internal sealed class DictionaryEntryActions(IDictionaryEntryAccess dictionaries)
{
    /// <summary>
    /// Returns the entry Visual Studio looks up; the stand-in when <paramref name="dictionary"/> is one.
    /// </summary>
    public object? GetEntry(object dictionary, object? key)
    {
        return dictionary is DroppedElement ? dictionary : dictionaries.GetEntry(dictionary, key);
    }

    /// <summary>
    /// Adds the entry Visual Studio adds, unless the dictionary or the entry is a stand-in.
    /// </summary>
    /// <param name="dictionary">The dictionary the action addresses.</param>
    /// <param name="key">The key of the entry.</param>
    /// <param name="getValue">Resolves the value of the entry; it is not asked for when the dictionary is a stand-in.</param>
    public void AddEntry(object dictionary, object? key, Func<object?> getValue)
    {
        if (TryGetValue(dictionary, getValue, out object? value))
        {
            dictionaries.AddEntry(dictionary, key, value);
        }
    }

    /// <summary>
    /// Replaces the value Visual Studio replaces, unless the dictionary or the value is a stand-in.
    /// </summary>
    /// <param name="dictionary">The dictionary the action addresses.</param>
    /// <param name="key">The key of the entry.</param>
    /// <param name="getValue">Resolves the new value; it is not asked for when the dictionary is a stand-in.</param>
    public void ReplaceEntry(object dictionary, object? key, Func<object?> getValue)
    {
        if (TryGetValue(dictionary, getValue, out object? value))
        {
            dictionaries.ReplaceEntry(dictionary, key, value);
        }
    }

    /// <summary>
    /// Removes the entry Visual Studio removes, unless the dictionary is a stand-in.
    /// </summary>
    public void RemoveEntry(object dictionary, object? key)
    {
        if (dictionary is not DroppedElement)
        {
            dictionaries.RemoveEntry(dictionary, key);
        }
    }

    // A value is only resolved for a live dictionary, and a value that stands in for a dropped element is not added.
    private static bool TryGetValue(object dictionary, Func<object?> getValue, out object? value)
    {
        value = null;
        if (dictionary is DroppedElement)
        {
            return false;
        }

        value = getValue();
        return value is not DroppedElement;
    }
}
