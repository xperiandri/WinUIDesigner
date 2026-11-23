// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Xml;
using System.Xml.Linq;
using WinUIDesigner.Markup;
using WinUIDesigner.Surface.Markup;

namespace WinUIDesigner.Surface.Services;

/// <summary>
/// Keeps the child indices of Visual Studio's document model and the live WinUI collections in step, although the
/// collections lack the elements the namespace policies dropped.
/// </summary>
/// <remarks>
/// The index translation itself is <see cref="ChildIndexTranslator"/>; this class gives it access to WinUI collections
/// and supplies the holes of prepared documents. Construction actions carry their holes as <see cref="DroppedElement"/>
/// stand-ins. Prepared XAML carries them in the rewrite: the rewriter reports where it removed elements, the owners of
/// those collections are marked for the XAML reader (<see cref="DroppedChildOwnerMarks"/>), and the collections of the
/// marked objects receive their holes once the document is loaded (<see cref="RegisterLoaded"/>).
/// </remarks>
internal sealed class DroppedChildren : IChildCollectionAccess
{
    private readonly Func<object, int, bool> _removeAt;

    /// <param name="track">Makes an object part of the document that is being built or edited, so that it is released with it.</param>
    /// <param name="removeAt">
    /// Removes the child at a live index and returns <see langword="false"/> when the index points past the end;
    /// by default <see cref="XamlRuntimeUtilities.RemoveChild"/>. The surface passes a removal that can also change
    /// the setters of a style in use.
    /// </param>
    public DroppedChildren(Action<object> track, Func<object, int, bool>? removeAt = null)
    {
        _removeAt = removeAt ?? XamlRuntimeUtilities.RemoveChild;
        Translator = new ChildIndexTranslator(this, track, message => WinUIDesignerLogger.LogWarning("Surface", message));
    }

    /// <summary>
    /// Gets the translator of the child indices.
    /// </summary>
    public ChildIndexTranslator Translator { get; }

    /// <summary>
    /// Gets the maps of the collections with dropped elements.
    /// </summary>
    public ChildIndexMaps Maps => Translator.Maps;

    /// <inheritdoc cref="ChildIndexTranslator.PlaceChild"/>
    public int? PlaceChild(object owner, int modelIndex, object? child, Func<object, int>? countPending = null)
    {
        return Translator.PlaceChild(owner, modelIndex, child, countPending);
    }

    /// <inheritdoc cref="ChildIndexTranslator.RemoveChild"/>
    public void RemoveChild(object owner, int modelIndex, Action<object>? flushPending = null)
    {
        Translator.RemoveChild(owner, modelIndex, flushPending);
    }

    /// <inheritdoc cref="ChildIndexTranslator.GetChild"/>
    public object? GetChild(object owner, int modelIndex, Action<object>? flushPending = null)
    {
        return Translator.GetChild(owner, modelIndex, flushPending);
    }

    /// <summary>
    /// Forgets the dropped elements of a collection property that is about to be cleared: Visual Studio adds the
    /// children again afterwards, the dropped ones included.
    /// </summary>
    public void ForgetCollection(object target, string fullPropertyName)
    {
        if (Maps.Count == 0)
        {
            return;
        }

        try
        {
            if (XamlRuntimeUtilities.GetPropertyValue(target, fullPropertyName) is { } collection)
            {
                Translator.Forget(collection);
            }
        }
        catch (Exception ex)
        {
            // Clearing reports its own failure; only the bookkeeping is skipped here.
            WinUIDesignerLogger.LogTrace("Surface", $"Child indices: the collection {fullPropertyName} of {target.GetType().FullName} was not found: {ex.Message}");
        }
    }

    /// <inheritdoc cref="ChildIndexTranslator.Release"/>
    public void Release(IEnumerable<object> released)
    {
        Translator.Release(released);
    }

    /// <inheritdoc cref="ChildIndexTranslator.Clear"/>
    public void Clear()
    {
        Translator.Clear();
    }

    /// <summary>
    /// Gives the collections of a loaded document the holes of the elements the namespace policies removed from them,
    /// and clears the marks of their owners.
    /// </summary>
    /// <param name="documentId">The document, for logging.</param>
    /// <param name="collections">The collections from <see cref="DroppedChildOwnerMarks.Mark"/>.</param>
    /// <param name="owners">The objects the XAML reader created for the marked elements, by mark.</param>
    /// <returns>The number of collections that received holes.</returns>
    /// <remarks>
    /// A collection that cannot be found keeps no map: its indices are used as Visual Studio sends them, as before.
    /// </remarks>
    public int RegisterLoaded(int documentId, IReadOnlyList<DroppedChildCollection> collections, IReadOnlyDictionary<string, DependencyObject> owners)
    {
        int registered = 0;
        foreach (DroppedChildCollection dropped in collections)
        {
            string description = DescribeElement(dropped.Owner, dropped.Property);
            if (!owners.TryGetValue(dropped.Mark, out DependencyObject? owner))
            {
                WinUIDesignerLogger.LogTrace("Surface", $"Child indices: document {documentId}: no object was created for {description}; its indices are used as they are.");
                continue;
            }

            object? collection;
            try
            {
                collection = ResolveCollection(owner, dropped.Property);
            }
            catch (Exception ex)
            {
                WinUIDesignerLogger.LogTrace("Surface", $"Child indices: document {documentId}: the collection of {description} was not found: {ex.Message}");
                continue;
            }

            if (collection is null || !IsIndexedCollection(collection))
            {
                WinUIDesignerLogger.LogTrace("Surface", $"Child indices: document {documentId}: {description} holds no indexed collection; its indices are used as they are.");
                continue;
            }

            ChildIndexMap map = Translator.Register(collection, dropped.Holes);
            registered++;
            WinUIDesignerLogger.LogTrace("Surface", $"Child indices: document {documentId}: {description} ({collection.GetType().Name}) has dropped elements at {map}.");
        }

        foreach (DependencyObject owner in owners.Values)
        {
            try
            {
                owner.ClearValue(DT.DroppedChildOwnerProperty);
            }
            catch (Exception)
            {
                // The mark only identified the object while it was loaded; leaving it set changes nothing.
            }
        }

        return registered;
    }

    /// <summary>
    /// Returns the collection Visual Studio's child actions address for the content or a property element of
    /// <paramref name="owner"/>, or <see langword="null"/> when that holds a single value.
    /// </summary>
    /// <remarks>
    /// A single value cannot have holes: two elements under complementary conditions as the content of a
    /// <c>Border</c> leave one child, which may itself be a panel whose children must not be mistaken for the
    /// collection that lost an element.
    /// </remarks>
    internal static object? ResolveCollection(DependencyObject owner, XName? property)
    {
        object? value;
        if (property is null)
        {
            string? contentProperty = FindContentPropertyName(owner.GetType());
            value = contentProperty is null
                ? XamlRuntimeUtilities.GetChildCollection(owner)
                : owner.GetType().GetProperty(contentProperty, BindingFlags.Public | BindingFlags.Instance)?.GetValue(owner);
        }
        else
        {
            // A property element is named Type.Member: Grid.ColumnDefinitions, or an attached property such as
            // VisualStateManager.VisualStateGroups on another type.
            string localName = property.LocalName;
            int separator = localName.IndexOf('.');
            string member = localName[(separator + 1)..];
            PropertyInfo? instanceProperty = owner.GetType().GetProperty(member, BindingFlags.Public | BindingFlags.Instance);
            value = instanceProperty?.CanRead == true
                ? instanceProperty.GetValue(owner)
                : GetAttachedValue(owner, localName[..separator], member, property.NamespaceName);
        }

        return value is not null && IsCollectionObject(value) ? XamlRuntimeUtilities.GetChildCollection(value) ?? value : null;
    }

    /// <summary>
    /// Returns whether <paramref name="value"/> is a collection itself, rather than an object that holds one.
    /// </summary>
    private static bool IsCollectionObject(object value)
    {
        if (value is IList)
        {
            return true;
        }

        foreach (Type interfaceType in value.GetType().GetInterfaces())
        {
            if (interfaceType.IsGenericType
                && (interfaceType.GetGenericTypeDefinition() == typeof(IList<>) || interfaceType.GetGenericTypeDefinition() == typeof(ICollection<>)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns the name of the XAML content property of <paramref name="type"/>, from its <see cref="ContentPropertyAttribute"/>.
    /// </summary>
    internal static string? FindContentPropertyName(Type type)
    {
        for (Type? current = type; current is not null; current = current.BaseType)
        {
            foreach (object attribute in current.GetCustomAttributes(typeof(ContentPropertyAttribute), inherit: false))
            {
                if (attribute is ContentPropertyAttribute { Name: { Length: > 0 } name })
                {
                    return name;
                }
            }
        }

        return null;
    }

    object IChildCollectionAccess.GetCollection(object owner)
    {
        return XamlRuntimeUtilities.GetChildCollection(owner) ?? owner;
    }

    int? IChildCollectionAccess.GetCount(object collection)
    {
        return XamlRuntimeUtilities.GetChildCount(collection);
    }

    bool IChildCollectionAccess.RemoveAt(object owner, int liveIndex)
    {
        return _removeAt(owner, liveIndex);
    }

    object? IChildCollectionAccess.GetAt(object owner, int liveIndex)
    {
        return XamlRuntimeUtilities.GetChild(owner, liveIndex);
    }

    private static object? GetAttachedValue(DependencyObject owner, string typeName, string member, string namespaceName)
    {
        Type? declaringType = null;
        string baseUri = XamlNamespaceUri.Parse(namespaceName).BaseUri;
        if (baseUri.StartsWith(XamlNamespaceUri.UsingScheme, StringComparison.Ordinal))
        {
            declaringType = XamlRuntimeUtilities.ResolveType(baseUri[XamlNamespaceUri.UsingScheme.Length..] + "." + typeName);
        }
        else if (baseUri == XamlNamespaces.Presentation)
        {
            declaringType = typeof(FrameworkElement).Assembly.GetExportedTypes().FirstOrDefault(candidate =>
                candidate.Name == typeName && candidate.Namespace?.StartsWith("Microsoft.UI.Xaml", StringComparison.Ordinal) == true);
        }

        MethodInfo? getter = declaringType?.GetMethods(BindingFlags.Public | BindingFlags.Static).FirstOrDefault(method =>
            method.Name == "Get" + member
            && method.GetParameters() is { Length: 1 } parameters
            && parameters[0].ParameterType.IsInstanceOfType(owner));
        return getter?.Invoke(null, [owner]);
    }

    private static bool IsIndexedCollection(object collection)
    {
        if (collection is IDictionary || collection is ResourceDictionary)
        {
            return false;
        }

        foreach (Type interfaceType in collection.GetType().GetInterfaces())
        {
            if (interfaceType.IsGenericType && interfaceType.GetGenericTypeDefinition() == typeof(IDictionary<,>))
            {
                return false;
            }
        }

        return XamlRuntimeUtilities.GetChildCount(collection) is not null;
    }

    private static string DescribeElement(XElement owner, XName? property)
    {
        string name = property is null ? $"<{owner.Name.LocalName}>" : $"<{owner.Name.LocalName}><{property.LocalName}>";
        IXmlLineInfo lineInfo = owner;
        return lineInfo.HasLineInfo()
            ? $"{name} (line {lineInfo.LineNumber.ToString(CultureInfo.InvariantCulture)})"
            : name;
    }
}
