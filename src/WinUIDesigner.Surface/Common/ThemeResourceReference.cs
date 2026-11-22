// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

namespace WinUIDesigner.Surface.Common;

/// <summary>
/// Carries a deferred theme-resource key from the instance builder.
/// The UWP instance builder sends this protocol object for <c>{ThemeResource}</c> instead of a WinRT type. Its
/// <c>ResourceKey</c> is the member of <see cref="StaticResourceReference"/> (Visual Studio backs both proxies with the
/// same type), and the key is initialized before the reference is evaluated at the owning property.
/// </summary>
internal sealed class ThemeResourceReference
{
    public const string ProtocolTypeName = "System.Windows.ThemeResourceExtension";

    public object? ResourceKey { get; set; }
}
