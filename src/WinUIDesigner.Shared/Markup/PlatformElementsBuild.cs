// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

namespace WinUIDesigner.Markup;

/// <summary>
/// How a document reaches the surface, given the elements it has under Uno platform prefixes.
/// </summary>
internal enum PlatformElementsBuild
{
    /// <summary>
    /// The prepared XAML that Visual Studio made has everything the surface needs, and the surface loads it.
    /// </summary>
    PreparedXaml,

    /// <summary>
    /// The document was prepared a second time, so that its prepared XAML keeps the raw elements under platform
    /// prefixes; the surface loads it and its namespace policies decide about those elements.
    /// </summary>
    PreparedAgain,

    /// <summary>
    /// The prepared XAML lost the elements under platform prefixes, so the document is built from construction
    /// actions, which are generated from a document model that leaves them out.
    /// </summary>
    ConstructionActions,
}
