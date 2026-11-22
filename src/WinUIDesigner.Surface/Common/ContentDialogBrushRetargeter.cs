// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;

namespace WinUIDesigner.Surface.Common;

/// <summary>The brush properties of a dialog as <see cref="ContentDialogBrushRetargeter"/> reads and writes them.</summary>
internal interface IDialogBrushProperties
{
    /// <summary>The value set on the dialog itself, by the document or by the surface, where there is one.</summary>
    bool TryGetLocalValue(string property, out object? value);

    /// <summary>The value that the dialog has: the one set on it, otherwise the one of its style.</summary>
    object? GetValue(string property);

    void SetValue(string property, object value);

    void ClearValue(string property);
}

/// <summary>
/// Gives a <c>ContentDialog</c> the brushes that <see cref="ContentDialogThemePolicy"/> chooses for the theme of the preview
/// in place of the ones of its style. A brush that the surface writes is set on the dialog itself, where a value of the
/// document stands too, so the retargeter remembers what it wrote: it takes that back before the brushes are chosen again,
/// and leaves a value that the document has set since. The brushes are chosen again when a theme changes and after every
/// batch of edits (an edit of the style, or a value of the document that is removed, gives the dialog another brush while
/// the themes stay); what was chosen for a brush is remembered as long as the themes stay, so that the resources are read
/// only for a brush that was not seen before.
/// </summary>
internal sealed class ContentDialogBrushRetargeter
{
    /// <summary>The brush properties of a dialog that its style sets from the resources.</summary>
    public static readonly IReadOnlyList<string> Properties = ["Background", "BorderBrush", "Foreground"];

    private readonly Dictionary<string, object> _written = new(StringComparer.Ordinal);
    private readonly Dictionary<object, BrushChoice> _choices = new(ReferenceEqualityComparer.Instance);
    private (string? Preview, string Application)? _themes;

    /// <summary>Forgets the brushes that it wrote and what it chose: the dialog is gone.</summary>
    public void Forget()
    {
        _written.Clear();
        _choices.Clear();
        _themes = null;
    }

    /// <summary>
    /// Chooses the brushes of <paramref name="dialog"/> for a preview with the theme <paramref name="previewTheme"/>
    /// (<see langword="null"/> where it has no theme of its own) of an application with the theme
    /// <paramref name="applicationTheme"/>, where the themes are not the ones that it chose for last or
    /// <paramref name="afterEdits"/> says that the document was edited since. <paramref name="collectDictionaries"/> is called
    /// once at most, and only where a brush needs the resources; <paramref name="describe"/> turns a value of the dialog into
    /// a <see cref="ThemeBrush"/> (<see langword="null"/> for a value that is not a brush); <paramref name="chosen"/> hears of
    /// each choice that is made, for the log.
    /// </summary>
    public void Refresh(
        IDialogBrushProperties dialog,
        string? previewTheme,
        string applicationTheme,
        bool afterEdits,
        Func<IReadOnlyList<ThemeDictionarySet>> collectDictionaries,
        Func<object, ThemeBrush?> describe,
        Action<string, ThemeBrush, BrushChoice>? chosen = null
    )
    {
        // The brushes were chosen for these themes, and nothing was edited since: they are as they should be.
        if (!afterEdits && _themes == (previewTheme, applicationTheme))
        {
            return;
        }

        // Other themes: what was chosen for the old ones does not hold.
        if (_themes != (previewTheme, applicationTheme))
        {
            _choices.Clear();
            _themes = (previewTheme, applicationTheme);
        }

        IReadOnlyList<ThemeDictionarySet>? dictionaries = null;
        foreach (string property in Properties)
        {
            // A brush that the surface wrote is taken back, so that the one of the style is chosen again; a value that the
            // document has set over it since is the document's.
            if (_written.Remove(property, out object? written)
                && dialog.TryGetLocalValue(property, out object? local)
                && ReferenceEquals(local, written))
            {
                dialog.ClearValue(property);
            }

            // A value that the document sets on the dialog itself stays, and so does every brush while the preview has the
            // theme of the application.
            if (dialog.TryGetLocalValue(property, out _)
                || !ContentDialogThemePolicy.PreviewThemeDiffers(previewTheme, applicationTheme)
                || dialog.GetValue(property) is not { } value)
            {
                continue;
            }

            // A brush of the style that was not seen for these themes: the policy chooses, from the theme dictionaries.
            if (!_choices.TryGetValue(value, out BrushChoice choice))
            {
                if (describe(value) is not { } current)
                {
                    continue;
                }

                dictionaries ??= collectDictionaries();
                choice = ContentDialogThemePolicy.Choose(current, setByDocument: false, previewTheme, applicationTheme, dictionaries);
                _choices[value] = choice;
                chosen?.Invoke(property, current, choice);
            }

            if (choice.Brush is { } target)
            {
                dialog.SetValue(property, target.Brush);
                _written[property] = target.Brush;
            }
        }
    }
}
