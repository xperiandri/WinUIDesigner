// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using Microsoft.UI.Xaml.Markup;
using WinUIDesigner;

#pragma warning disable IDE0130 // Namespace does not match folder structure
// Match the VS UWP designer contract: its instance builders send d:DesignInstance as "XSurfUwp.DesignInstance, UwpSurface".
namespace XSurfUwp;
#pragma warning restore IDE0130 // Namespace does not match folder structure

/// <summary>
/// Stands in for <c>d:DesignInstance</c> on the construction-action path. Visual Studio sets <see cref="Type"/> to the
/// assembly-qualified name of the type and assigns the extension to <see cref="FrameworkElement.DataContext"/>; the
/// surface sets the value that <see cref="ProvideValue()"/> provides (<see cref="XamlRuntimeUtilities.SetPropertyValue"/>).
/// </summary>
public sealed partial class DesignInstance : MarkupExtension
{
    /// <summary>
    /// Gets or sets the type of the design-time data context: the assembly-qualified name that Visual Studio sends, or
    /// a <see cref="System.Type"/>.
    /// </summary>
    public object? Type { get; set; }

    /// <summary>
    /// Gets or sets whether the designer may create the type; when it may not, the data context stays empty.
    /// </summary>
    public bool IsDesignTimeCreatable { get; set; }

    /// <summary>
    /// Gets or sets whether the data context is a list that holds one instance of the type.
    /// </summary>
    public bool CreateList { get; set; }

    /// <summary>
    /// Creates the data context, or returns <see langword="null"/> when the type cannot be created. Never throws: a
    /// design-time data context must not fail the document.
    /// </summary>
    protected override object? ProvideValue()
    {
        object? value = DesignInstanceFactory.Create(Type, IsDesignTimeCreatable, CreateList, XamlRuntimeUtilities.ResolveType, out Exception? failure);
        if (failure is not null)
        {
            WinUIDesignerLogger.LogWarning("Surface", $"d:DesignInstance could not create '{Type}'; the design-time data context stays empty.", failure);
        }

        return value;
    }
}
