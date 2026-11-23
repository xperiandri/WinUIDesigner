// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

namespace WinUIDesigner.Surface.Services;

/// <summary>
/// Gives <see cref="DictionaryEntryActions"/> access to the live dictionaries: a style in use is changed through
/// XAML Diagnostics, any other dictionary directly.
/// </summary>
/// <param name="styleEdits">Applies the edits of styles that are in use.</param>
internal sealed class LiveDictionaryEntries(StyleEditService styleEdits) : IDictionaryEntryAccess
{
    public object? GetEntry(object dictionary, object? key)
    {
        return XamlRuntimeUtilities.GetDictionaryEntry(dictionary, key);
    }

    public void AddEntry(object dictionary, object? key, object? value)
    {
        if (!styleEdits.TryAddDictionaryEntry(dictionary, key, value))
        {
            XamlRuntimeUtilities.SetDictionaryEntry(dictionary, key, value);
        }
    }

    public void ReplaceEntry(object dictionary, object? key, object? value)
    {
        if (!styleEdits.TryReplaceDictionaryEntry(dictionary, key, value))
        {
            XamlRuntimeUtilities.SetDictionaryEntry(dictionary, key, value);
        }
    }

    public void RemoveEntry(object dictionary, object? key)
    {
        if (!styleEdits.TryRemoveDictionaryEntry(dictionary, key))
        {
            XamlRuntimeUtilities.RemoveDictionaryEntry(dictionary, key);
        }
    }
}
