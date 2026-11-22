// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Diagnostics;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.InstanceBuilders.Shared;

namespace WinUIDesigner.Surface.Services;

/// <summary>
/// Evaluates the <c>{ThemeResource}</c> references of construction actions for the theme of their owner, and evaluates
/// the properties set from them again when that theme changes: new artboard colours (the preview theme), another theme
/// requested by the project's application, a <c>RequestedTheme</c> edited in the document, or content that comes
/// under a surface. WinUI does the same for the references that its own XAML reader creates.
/// </summary>
internal sealed partial class SurfaceService
{
    private readonly ThemeResourceRegistry _themeReferences = new();

    // The theme of the last artboard colours: Visual Studio's artboard background is the same for every document.
    private ElementTheme? _lastPreviewTheme;

    // The {ThemeResource} that ResolveActionValue evaluated for the action being executed.
    private ThemeResourceUse? _evaluatedThemeResource;

    // A RequestedTheme changed, or content came under a surface, during the action batch being executed.
    private bool _themeReferencesNeedReapply;

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
        _evaluatedThemeResource = new(owner, key!, theme.Theme);

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

    /// <summary>
    /// Keeps the registry in step with an executed action: a property set from a <c>{ThemeResource}</c> is remembered,
    /// a property set otherwise or cleared is forgotten, and a new <c>RequestedTheme</c> has the references evaluated
    /// again at the end of the batch.
    /// </summary>
    private void TrackThemeResourceProperty(XamlAction action)
    {
        ThemeResourceUse? evaluated = _evaluatedThemeResource;
        _evaluatedThemeResource = null;

        // A theme requested on the owner moves the theme of the references below it.
        if (ThemeResourceTracking.Track(_themeReferences, action, evaluated, ResolveObject, _objectIdentity.GetDocumentId))
        {
            _themeReferencesNeedReapply = true;
        }
    }

    /// <summary>
    /// Evaluates again the remembered properties whose owner has another theme now. A failure is logged per property and
    /// never thrown; the property keeps its value.
    /// </summary>
    /// <returns><see langword="true"/> when a property was given a new value.</returns>
    private bool ReapplyThemeReferences(string reason)
    {
        _themeReferencesNeedReapply = false;

        // Nothing is remembered: there is nothing to evaluate again.
        if (_themeReferences.Count == 0)
        {
            return false;
        }

        ThemeResourceReapplyResult result;
        try
        {
            result = _themeReferences.Reapply(
                entry => _resourceScopes.DecideTheme(entry.Owner, GetThemePreview(entry.Owner)).Theme,
                TryReapplyThemeReference,
                LogThemeReferenceFailure
            );
        }
        catch (Exception exception)
        {
            // Whatever else goes wrong: the request that asked for the pass must not fail with it.
            WinUIDesignerLogger.LogWarning("Surface", $"The ThemeResource references were not evaluated again after {reason}.", exception);
            return false;
        }

        WinUIDesignerLogger.LogTrace("Surface", $"ThemeResource references after {reason}: {result.Applied} evaluated again, {result.Failed} kept their value, {result.Visited - result.Applied - result.Failed} already in their theme.");
        return result.Applied > 0;
    }

    /// <summary>
    /// Logs an entry that a pass could not evaluate again because deciding its theme threw (that reads the live objects of
    /// its owner). The entry keeps its value and the pass goes on with the next one.
    /// </summary>
    private static void LogThemeReferenceFailure(ThemeResourceEntry entry, Exception exception)
    {
        WinUIDesignerLogger.LogTrace("Surface", $"ThemeResource '{entry.Key}' of a {entry.Owner.GetType().Name} keeps its {entry.Theme} value, it could not be evaluated again: {exception.Message}");
    }

    private bool TryReapplyThemeReference(ThemeResourceEntry entry, string theme)
    {
        try
        {
            ThemeResourceMatch match = _resourceScopes.ResolveThemeResource(entry.Owner, entry.Key, theme);
            XamlRuntimeUtilities.SetPropertyValue(entry.Owner, entry.Property, match.Value);
            return true;
        }
        catch (Exception exception)
        {
            // The Setter of a style that is in use is sealed: the public API refuses the new value.
            WinUIDesignerLogger.LogTrace("Surface", $"ThemeResource '{entry.Key}' of a {entry.Owner.GetType().Name} keeps its {entry.Theme} value instead of the {theme} one: {exception.Message}");
            return false;
        }
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
