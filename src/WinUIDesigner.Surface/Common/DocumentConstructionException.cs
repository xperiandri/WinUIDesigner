// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.InstanceBuilders.Shared;

namespace WinUIDesigner.Surface.Common;

/// <summary>
/// Reports which XAML action failed while constructing a document.
/// </summary>
/// <param name="cause">The exception of the action.</param>
/// <param name="action">The action that failed.</param>
/// <param name="replacedFailures">
/// The failures of the objects that a fallback took the place of before the action failed. They are reported first,
/// as the document is not built: otherwise Visual Studio never learns why those objects were replaced.
/// </param>
internal sealed class DocumentConstructionException(Exception cause, XamlAction action, IEnumerable<ActionError>? replacedFailures = null)
    : Exception("A document construction action failed.", cause)
{
    public string SerializedErrors { get; } = ActionErrorJsonSerializer.Serialize([
        .. replacedFailures ?? [],
        new ActionError { XamlAction = action, Error = cause.ToString() },
    ]);
}
