// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Diagnostics;

namespace WinUIDesigner.Surface.Services;

/// <summary>
/// Evaluates the <c>{ThemeResource}</c> references of construction actions for the theme of their owner.
/// </summary>
internal sealed partial class SurfaceService
{
    // The theme of the last artboard colours: Visual Studio's artboard background is the same for every document.
    private ElementTheme? _lastPreviewTheme;

    private int _themeResourceEvaluations;
    private long _themeResourceEvaluationTicks;

    /// <summary>
    /// Evaluates a <c>{ThemeResource}</c> key at <paramref name="owner"/>: decides the theme, then looks the key up for
    /// it. A missing key fails the action, as it does for <c>{StaticResource}</c>.
    /// </summary>
    private object? EvaluateThemeResource(object owner, object? key)
    {
        long started = Stopwatch.GetTimestamp();
        ThemeChoice theme = _resourceScopes.DecideTheme(owner, GetThemePreview(owner));
        ThemeResourceMatch match = _resourceScopes.ResolveThemeResource(owner, key, theme.Theme);
        _themeResourceEvaluations++;
        _themeResourceEvaluationTicks += Stopwatch.GetTimestamp() - started;

        if (WinUIDesignerLogger.IsVerboseLoggingEnabled)
        {
            string found = match.IsFrameworkFallback
                ? match.Path
                : $"{match.Path} in {_resourceScopes.DescribeThemeResourceScope(owner, match)}";
            WinUIDesignerLogger.LogTrace("Surface", $"ThemeResource '{key}' for a {owner.GetType().Name}: {theme.Theme} theme ({theme.Source}), {found}.");
        }

        return match.Value;
    }

    /// <summary>The themes that the surface knows for <paramref name="owner"/> apart from the walk of its scopes.</summary>
    private ThemePreview GetThemePreview(object owner)
    {
        string? surfaceTheme = _surfaces.TryGetValue(_objectIdentity.GetDocumentId(owner), out DesignerSurface? surface)
            ? ResourceScopeService.ToThemeName(surface.PreviewTheme)
            : null;
        string? lastPreviewTheme = _lastPreviewTheme is { } theme ? ResourceScopeService.ToThemeName(theme) : null;
        string applicationTheme = Application.Current.RequestedTheme == ApplicationTheme.Dark ? ThemeResourceThemes.Dark : ThemeResourceThemes.Light;
        return new(surfaceTheme, lastPreviewTheme, ResourceScopeService.ToThemeName(_appRequestedTheme), applicationTheme);
    }

    /// <summary>Logs how many <c>{ThemeResource}</c> references a document construction evaluated, and how long it took.</summary>
    private ThemeResourceEvaluationTrace TraceThemeResourceEvaluations(int documentId) => new(this, documentId);

    private sealed class ThemeResourceEvaluationTrace(SurfaceService service, int documentId) : IDisposable
    {
        private readonly int _evaluations = service._themeResourceEvaluations;
        private readonly long _ticks = service._themeResourceEvaluationTicks;

        public void Dispose()
        {
            int evaluations = service._themeResourceEvaluations - _evaluations;
            if (evaluations > 0)
            {
                double milliseconds = (service._themeResourceEvaluationTicks - _ticks) * 1000.0 / Stopwatch.Frequency;
                WinUIDesignerLogger.LogTrace("Surface", $"BuildDocument ({documentId}) evaluated {evaluations} ThemeResource reference(s) in {milliseconds:0.#} ms.");
            }
        }
    }
}
