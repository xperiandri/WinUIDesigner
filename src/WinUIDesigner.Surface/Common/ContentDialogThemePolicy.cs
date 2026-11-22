// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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
    /// Whether the preview has a theme of its own that is not the theme of the application: only then can a brush that a
    /// style takes from the resources of the application be the one of another theme.
    /// </summary>
    public static bool PreviewThemeDiffers([NotNullWhen(true)] string? previewTheme, string applicationTheme)
    {
        return previewTheme is not null && previewTheme != applicationTheme;
    }

    /// <summary>
    /// What <paramref name="current"/>, a brush of the dialog, becomes in a preview with the theme
    /// <paramref name="previewTheme"/> (<see langword="null"/> where the preview has no theme of its own) of an application
    /// with the theme <paramref name="applicationTheme"/>. <paramref name="setByDocument"/> says that the document sets the
    /// brush on the dialog itself. Only a brush that comes from the theme of the application is replaced, by the brush that
    /// the theme of the preview has under the same key; a brush of the theme of the preview, a brush that no theme dictionary
    /// has (a colour that the document or the style gives, or a brush that code made) and a brush of the document stay.
    /// </summary>
    public static BrushChoice Choose(ThemeBrush current, bool setByDocument, string? previewTheme, string applicationTheme, IReadOnlyList<ThemeDictionarySet> dictionaries)
    {
        // The document sets the brush on the dialog itself: it is the brush that the document asks for, in every theme.
        if (setByDocument)
        {
            return BrushChoice.Keep("the document sets it on the dialog");
        }

        // The preview has no theme of its own, or the one of the application: the style gave the brushes of that theme.
        if (!PreviewThemeDiffers(previewTheme, applicationTheme))
        {
            return BrushChoice.Keep("the preview has the theme of the application");
        }

        ThemeEvidence evidence = Examine(current, previewTheme, applicationTheme, dictionaries);

        // The brush of the theme of the preview under a key that the theme of the application has another brush under.
        if (evidence.OfPreviewTheme is { } previewKey)
        {
            return BrushChoice.Keep($"it is the brush of the {previewTheme} theme under '{previewKey}'");
        }

        // The brush of the theme of the application under a key: the brush of the theme of the preview under that key.
        if (PreferredKey(evidence.Counterparts.Select(counterpart => counterpart.Key)) is { } key)
        {
            ThemeBrush counterpart = evidence.Counterparts.First(candidate => candidate.Key == key).Brush;
            return BrushChoice.Take(counterpart, $"it is the brush of the {applicationTheme} theme under '{key}'");
        }

        // Both themes draw the key that has it alike: it is right in either.
        if (evidence.DrawnAlike is { } alikeKey)
        {
            return BrushChoice.Keep($"both themes draw '{alikeKey}' alike");
        }

        // A reference that both themes have the same object under, or that one theme has alone, was resolved for the theme of
        // the application (a reference to a brush of a theme dictionary of another file): its colour tells the key.
        if (evidence.Reference is { } reference)
        {
            return ChooseByColour(current, previewTheme, applicationTheme, dictionaries, reference);
        }

        // In no theme dictionary: nothing says that it is a brush of the theme of the application.
        return BrushChoice.Keep("no theme dictionary has it");
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

    /// <summary>
    /// What the theme dictionaries that have both themes say about the object of <paramref name="current"/>. Only the
    /// dictionaries of the theme of the application and of the theme of the preview count: a brush of another theme
    /// (<c>HighContrast</c>) is not what the style took.
    /// </summary>
    private static ThemeEvidence Examine(ThemeBrush current, string previewTheme, string applicationTheme, IReadOnlyList<ThemeDictionarySet> dictionaries)
    {
        ThemeEvidence evidence = new(current);
        foreach (ThemeDictionarySet set in dictionaries)
        {
            if (set.For(applicationTheme) is not { } application || set.For(previewTheme) is not { } preview)
            {
                continue;
            }

            foreach (KeyValuePair<string, ThemeBrush> entry in preview)
            {
                if (ReferenceEquals(entry.Value.Brush, current.Brush))
                {
                    evidence.Add(entry.Key, application, isPreviewTheme: true);
                }
            }

            foreach (KeyValuePair<string, ThemeBrush> entry in application)
            {
                if (ReferenceEquals(entry.Value.Brush, current.Brush))
                {
                    evidence.Add(entry.Key, preview, isPreviewTheme: false);
                }
            }
        }

        return evidence;
    }

    /// <summary>
    /// A reference was resolved for the theme of the application: the key that has the colour of <paramref name="current"/>
    /// in the dictionary of the theme of the application and another colour in the one of the theme of the preview gives
    /// the brush (the brushes of a theme dictionary of another file, as the palette of Uno Material).
    /// </summary>
    private static BrushChoice ChooseByColour(ThemeBrush current, string previewTheme, string applicationTheme, IReadOnlyList<ThemeDictionarySet> dictionaries, string reference)
    {
        // A brush that is not a solid colour has no colour to look for.
        if (current.Color is null)
        {
            return BrushChoice.Keep($"'{reference}' was resolved for one theme, and it is not a solid colour");
        }

        List<(string Key, ThemeBrush Brush)> candidates = [];
        foreach (ThemeDictionarySet set in dictionaries)
        {
            if (set.For(applicationTheme) is not { } application || set.For(previewTheme) is not { } preview)
            {
                continue;
            }

            foreach (KeyValuePair<string, ThemeBrush> entry in application)
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

        if (PreferredKey(candidates.Select(candidate => candidate.Key)) is not { } key)
        {
            return BrushChoice.Keep($"'{reference}' was resolved for one theme, and no key has its colour in the {applicationTheme} theme and another one in the {previewTheme} theme");
        }

        ThemeBrush brush = candidates.First(candidate => candidate.Key == key).Brush;
        return BrushChoice.Take(brush, $"'{reference}' was resolved for the {applicationTheme} theme, whose '{key}' has its colour");
    }

    /// <summary>The keys that the theme dictionaries have the object of a brush under, by what each of them says.</summary>
    private sealed class ThemeEvidence(ThemeBrush current)
    {
        /// <summary>A key of the theme of the preview that the theme of the application has another brush under.</summary>
        public string? OfPreviewTheme { get; private set; }

        /// <summary>The keys of the theme of the application, with the brush of the theme of the preview under each.</summary>
        public List<(string Key, ThemeBrush Brush)> Counterparts { get; } = [];

        /// <summary>A key that the other theme has a brush under that draws alike.</summary>
        public string? DrawnAlike { get; private set; }

        /// <summary>A key that both themes have the same object under, or that the other theme does not have.</summary>
        public string? Reference { get; private set; }

        /// <summary>The object is under <paramref name="key"/> in one theme; <paramref name="other"/> is the dictionary of the other theme.</summary>
        public void Add(string key, IReadOnlyDictionary<string, ThemeBrush> other, bool isPreviewTheme)
        {
            // The other theme has the same object under the key, or no brush at all: a reference resolved for one theme.
            if (!other.TryGetValue(key, out ThemeBrush otherBrush) || ReferenceEquals(otherBrush.Brush, current.Brush))
            {
                Reference ??= key;
                return;
            }

            // The other theme has a brush that draws alike under the key: the brush is right in either theme.
            if (otherBrush.DrawsLike(current))
            {
                DrawnAlike ??= key;
                return;
            }

            // The theme of the preview has it, and the theme of the application another brush: it is the brush of the preview.
            if (isPreviewTheme)
            {
                OfPreviewTheme ??= key;
                return;
            }

            // The theme of the application has it, and the theme of the preview another brush: that one takes its place.
            Counterparts.Add((key, otherBrush));
        }
    }
}
