// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using WinUIDesigner.Markup;
using WinUIDesigner.Surface.Markup;

namespace WinUIDesigner.Surface.Services;

internal sealed partial class SurfaceService
{
    /// <summary>
    /// Configures which Uno platform the surface previews.
    /// </summary>
    private void ConfigurePlatformPrefixPolicy(XamlNamespaceRewriteOptions options)
    {
        // The surface is a WinUI process and previews the Windows flavor of the markup: members under a platform
        // prefix that is bound to the presentation namespace (win:, not_android:) become plain members, members
        // under one that is bound to an Uno namespace (android:, not_win:) are dropped. The classification is the
        // same for every target, but another target is only shown by prepared XAML that still has the raw
        // elements: see UnoPlatformPrefixes.
        options.TargetPlatform = UnoPlatformPrefixes.DefaultTargetPlatform;
    }
}
