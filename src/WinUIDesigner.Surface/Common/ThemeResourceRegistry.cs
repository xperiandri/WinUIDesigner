// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;

namespace WinUIDesigner.Surface.Common;

/// <summary>A property whose value came from a <c>{ThemeResource}</c>, and the theme it was evaluated in.</summary>
internal readonly record struct ThemeResourceEntry(object Owner, string Property, object Key, string Theme);

/// <summary>What one pass of <see cref="ThemeResourceRegistry.Reapply"/> did.</summary>
/// <param name="Visited">The entries it looked at: every entry, once.</param>
/// <param name="Applied">The entries evaluated again because their theme changed.</param>
/// <param name="Failed">The entries whose new theme could not be decided or whose new value could not be applied; they keep the old theme.</param>
internal readonly record struct ThemeResourceReapplyResult(int Visited, int Applied, int Failed);

/// <summary>
/// Remembers the properties that the surface set from a <c>{ThemeResource}</c>, so that they can be evaluated again when
/// the theme of their owner changes (WinUI does that for the references that its own XAML reader creates). An entry is
/// forgotten when the same property is set or cleared again and when its owner is released with its document.
/// </summary>
internal sealed class ThemeResourceRegistry
{
    private readonly Dictionary<(object Owner, string Property), ThemeResourceEntry> _entries = new(EntryKeyComparer.Instance);

    public int Count => _entries.Count;

    public IReadOnlyCollection<ThemeResourceEntry> Entries => _entries.Values;

    /// <summary>
    /// Records that <paramref name="property"/> of <paramref name="owner"/> holds the value of <paramref name="key"/>
    /// in <paramref name="theme"/>, in place of what the property held before.
    /// </summary>
    public void Record(object owner, string property, object key, string theme)
    {
        _entries[(owner, property)] = new(owner, property, key, theme);
    }

    /// <summary>Forgets the entry of a property that was set to another value or cleared.</summary>
    public bool Forget(object owner, string property) => _entries.Remove((owner, property));

    /// <summary>Forgets the entries of owners that were released with their document.</summary>
    public void ForgetOwners(IReadOnlySet<object> released)
    {
        // Nothing was released, or nothing is remembered.
        if (released.Count == 0 || _entries.Count == 0)
        {
            return;
        }

        foreach ((object Owner, string Property) entryKey in new List<(object Owner, string Property)>(_entries.Keys))
        {
            if (released.Contains(entryKey.Owner))
            {
                _entries.Remove(entryKey);
            }
        }
    }

    public void Clear() => _entries.Clear();

    /// <summary>
    /// Visits every entry once. An entry whose theme is still the one that <paramref name="decideTheme"/> gives is left
    /// alone; the others are handed to <paramref name="apply"/> with the new theme and keep it when it returns
    /// <see langword="true"/>. An entry whose theme cannot be decided, or whose <paramref name="apply"/> returns
    /// <see langword="false"/> or throws, keeps its old theme and is tried again by the next pass; an exception goes to
    /// <paramref name="onFailure"/>, and the pass goes on with the next entry.
    /// </summary>
    public ThemeResourceReapplyResult Reapply(
        Func<ThemeResourceEntry, string> decideTheme,
        Func<ThemeResourceEntry, string, bool> apply,
        Action<ThemeResourceEntry, Exception>? onFailure = null)
    {
        int applied = 0;
        int failed = 0;
        var keys = new List<(object Owner, string Property)>(_entries.Keys);
        foreach ((object Owner, string Property) entryKey in keys)
        {
            // An earlier apply of this pass can have forgotten or replaced the entry.
            if (!_entries.TryGetValue(entryKey, out ThemeResourceEntry entry))
            {
                continue;
            }

            try
            {
                // Already evaluated in that theme: Visual Studio sends the artboard colours again with every new surface.
                string theme = decideTheme(entry);
                if (theme == entry.Theme)
                {
                    continue;
                }

                if (apply(entry, theme))
                {
                    _entries[entryKey] = entry with { Theme = theme };
                    applied++;
                }
                else
                {
                    failed++;
                }
            }
            catch (Exception exception)
            {
                // The owner's live objects can fail to answer: this entry keeps its value and must not stop the others.
                failed++;
                onFailure?.Invoke(entry, exception);
            }
        }

        return new(keys.Count, applied, failed);
    }

    /// <summary>Owners compare by reference (two equal brushes are two owners); property names compare ordinally.</summary>
    private sealed class EntryKeyComparer : IEqualityComparer<(object Owner, string Property)>
    {
        public static EntryKeyComparer Instance { get; } = new();

        public bool Equals((object Owner, string Property) x, (object Owner, string Property) y)
        {
            return ReferenceEquals(x.Owner, y.Owner) && string.Equals(x.Property, y.Property, StringComparison.Ordinal);
        }

        public int GetHashCode((object Owner, string Property) obj)
        {
            return HashCode.Combine(ReferenceEqualityComparer.Instance.GetHashCode(obj.Owner), StringComparer.Ordinal.GetHashCode(obj.Property));
        }
    }
}
