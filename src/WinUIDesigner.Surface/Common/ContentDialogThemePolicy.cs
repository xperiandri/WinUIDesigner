// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;
using System.Linq;

namespace WinUIDesigner.Surface.Common;

/// <summary>
/// A brush as the choice of the brushes of a <c>ContentDialog</c> sees it: the object itself, which is compared by
/// reference, and the colour (ARGB) and opacity of a solid colour (<see cref="Color"/> is <see langword="null"/> for a
/// brush of another kind).
/// </summary>
internal readonly record struct ThemeBrush(object Brush, uint? Color, double Opacity)
{
    /// <summary>Whether two brushes draw alike: they are the same object, or solid colours of the same colour and opacity.</summary>
    public bool DrawsLike(ThemeBrush other)
    {
        return ReferenceEquals(Brush, other.Brush)
            || (Color is { } color && other.Color == color && Math.Abs(Opacity - other.Opacity) < 0.001);
    }
}

/// <summary>
/// The theme dictionaries of one resource dictionary by the key that each of them has in <c>ThemeDictionaries</c>
/// (<c>Default</c>, <c>Light</c>, <c>HighContrast</c>, <c>Dark</c>), each with its brushes by key.
/// </summary>
internal sealed class ThemeDictionarySet(IReadOnlyDictionary<string, IReadOnlyDictionary<string, ThemeBrush>> themes)
{
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, ThemeBrush>> Themes { get; } = themes;

    /// <summary>The dictionary that a theme takes its brushes from, or <see langword="null"/> where there is none.</summary>
    public IReadOnlyDictionary<string, ThemeBrush>? For(string theme)
    {
        foreach (string key in ContentDialogThemePolicy.ThemeKeys(theme))
        {
            if (Themes.TryGetValue(key, out IReadOnlyDictionary<string, ThemeBrush>? brushes))
            {
                return brushes;
            }
        }

        return null;
    }
}

/// <summary>
/// What a brush of the dialog becomes: <see cref="Brush"/> takes its place, or the dialog keeps it where that is
/// <see langword="null"/>. <see cref="Reason"/> says why, for the log.
/// </summary>
internal readonly record struct BrushChoice(ThemeBrush? Brush, string Reason)
{
    public static BrushChoice Keep(string reason) => new(null, reason);

    public static BrushChoice Take(ThemeBrush brush, string reason) => new(brush, reason);
}

/// <summary>
/// Chooses the brushes that a <c>ContentDialog</c> shows in the preview. The surface shows the dialog in the popup layer of
/// the island, and a style that takes its brushes from the resources of the application gets the ones of the theme of the
/// application, which in the process of the surface is the theme of the system, not the theme of the preview. The input is
/// plain data (see <see cref="ThemeBrush"/> and <see cref="ThemeDictionarySet"/>), so that tests reach the choice without
/// a XAML island.
/// </summary>
internal static class ContentDialogThemePolicy
{
    /// <summary>The light theme, as a key of <c>ThemeDictionaries</c>.</summary>
    public const string Light = "Light";

    /// <summary>The dark theme, as a key of <c>ThemeDictionaries</c> (see <see cref="ThemeKeys"/>).</summary>
    public const string Dark = "Dark";

    /// <summary>
    /// The keys that a theme has in <c>ThemeDictionaries</c>: the dark theme of Windows is <c>Default</c> (there is no
    /// <c>Dark</c> in the dictionaries of WinUI, nor in those of Uno Material), and the palette of an app may say
    /// <c>Dark</c>.
    /// </summary>
    public static string[] ThemeKeys(string theme) => theme == Dark ? [Dark, "Default"] : [theme];

    /// <summary>
    /// What <paramref name="current"/>, a brush of the dialog, becomes in a preview with the theme
    /// <paramref name="previewTheme"/> (<see langword="null"/> where the preview has no theme of its own) of an application
    /// with the theme <paramref name="applicationTheme"/>. <paramref name="setByDocument"/> says that the document sets the
    /// brush on the dialog itself. The brush is looked for in the theme dictionaries of the resources: the key that has the
    /// same object, or else the key that has its colour in the dictionary of another theme and another colour in the one of
    /// the theme of the preview, gives the brush of the theme of the preview.
    /// </summary>
    public static BrushChoice Choose(ThemeBrush current, bool setByDocument, string? previewTheme, string applicationTheme, IReadOnlyList<ThemeDictionarySet> dictionaries)
    {
        if (previewTheme is null)
        {
            return BrushChoice.Keep("the preview has no theme of its own");
        }

        ThemeBrush? target = FindSameObject(current, previewTheme, dictionaries, out string key);
        string how = $"the same object, key '{key}'";

        // The object of the dictionary of the theme itself says nothing: the brush that it is is the brush of the dialog.
        if (target is { } found && ReferenceEquals(found.Brush, current.Brush))
        {
            target = null;
        }

        if (target is null && current.Color is not null)
        {
            // Not the object of any dictionary (the style took a copy, or a brush that code made): the key is the one that has
            // this colour in the dictionaries of the other themes and another colour in the one of this theme.
            target = FindByColour(current, previewTheme, dictionaries, out key);
            how = $"its colour, key '{key}'";
        }

        return target is { } chosen && !ReferenceEquals(chosen.Brush, current.Brush)
            ? BrushChoice.Take(chosen, how)
            : BrushChoice.Keep($"no brush of the {previewTheme} theme was found for it");
    }

    /// <summary>
    /// The key that a brush is taken from where several keys qualify: the name that a surface has in the Material palette
    /// first, then any other name of a surface, then the shortest name, which is the least specific (the brushes of WinUI,
    /// which many are white, come first in the resources), and the first in ordinal order where that is not enough.
    /// </summary>
    public static string? PreferredKey(IEnumerable<string> keys)
    {
        return keys
            .OrderBy(key => key.Equals("SurfaceBrush", StringComparison.Ordinal) ? 0 : key.StartsWith("Surface", StringComparison.Ordinal) ? 1 : 2)
            .ThenBy(key => key.Length)
            .ThenBy(key => key, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static ThemeBrush? FindSameObject(ThemeBrush current, string previewTheme, IReadOnlyList<ThemeDictionarySet> dictionaries, out string key)
    {
        foreach (ThemeDictionarySet set in dictionaries)
        {
            if (set.For(previewTheme) is not { } preview)
            {
                continue;
            }

            foreach (IReadOnlyDictionary<string, ThemeBrush> theme in set.Themes.Values)
            {
                foreach (KeyValuePair<string, ThemeBrush> entry in theme)
                {
                    if (ReferenceEquals(entry.Value.Brush, current.Brush) && preview.TryGetValue(entry.Key, out ThemeBrush found))
                    {
                        key = entry.Key;
                        return found;
                    }
                }
            }
        }

        key = string.Empty;
        return null;
    }

    private static ThemeBrush? FindByColour(ThemeBrush current, string previewTheme, IReadOnlyList<ThemeDictionarySet> dictionaries, out string key)
    {
        List<(string Key, ThemeBrush Brush)> candidates = [];
        foreach (ThemeDictionarySet set in dictionaries)
        {
            if (set.For(previewTheme) is not { } preview)
            {
                continue;
            }

            foreach (KeyValuePair<string, IReadOnlyDictionary<string, ThemeBrush>> theme in set.Themes)
            {
                if (ThemeKeys(previewTheme).Contains(theme.Key))
                {
                    continue;
                }

                foreach (KeyValuePair<string, ThemeBrush> entry in theme.Value)
                {
                    if (entry.Value.Color is not null
                        && entry.Value.DrawsLike(current)
                        && preview.TryGetValue(entry.Key, out ThemeBrush other)
                        && other.Color is not null
                        && !other.DrawsLike(entry.Value))
                    {
                        candidates.Add((entry.Key, other));
                    }
                }
            }
        }

        key = PreferredKey(candidates.Select(candidate => candidate.Key)) ?? string.Empty;
        string preferred = key;
        return candidates.Where(candidate => candidate.Key == preferred).Select(candidate => (ThemeBrush?)candidate.Brush).FirstOrDefault();
    }
}
