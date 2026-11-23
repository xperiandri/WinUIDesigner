// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace WinUIDesigner.Surface.Runtime;

/// <summary>
/// The rules by which a C# projection of a Windows Runtime type answers what the metadata of the type would answer.
/// </summary>
/// <remarks>
/// <para>
/// The surface has no <c>.winmd</c> files beside it, so it asks the projection assemblies of the Windows App SDK
/// what the XAML reader would ask the metadata: whether a type is present, and whether it declares a property.
/// The projection differs from the metadata in a few places, and these rules close the gaps. They do not depend
/// on the Windows Runtime themselves: what makes a type or an interface a Windows Runtime type is passed in.
/// </para>
/// <para>
/// The metadata was compared with the projection of Windows App SDK 2.5.1 (<c>Microsoft.UI.Xaml.winmd</c> and
/// <c>Microsoft.WinUI.dll</c> of Microsoft.WindowsAppSDK.WinUI 2.3.9).
/// </para>
/// </remarks>
internal static class ProjectionMetadataRules
{
    // The types of Microsoft.UI.Xaml that the C# projection has no type of the same name for, with the
    // properties that the metadata declares for them. C#/WinRT maps ten of them to .NET types, and leaves out the
    // helper classes, whose members it adds to the structures.
    private static readonly Dictionary<string, string[]> _TypesWithoutProjection = new(StringComparer.Ordinal)
    {
        ["Microsoft.UI.Xaml.Input.ICommand"] = [],
        ["Microsoft.UI.Xaml.Data.INotifyPropertyChanged"] = [],
        ["Microsoft.UI.Xaml.Data.PropertyChangedEventArgs"] = ["PropertyName"],
        ["Microsoft.UI.Xaml.Data.PropertyChangedEventHandler"] = [],
        ["Microsoft.UI.Xaml.Data.INotifyDataErrorInfo"] = ["HasErrors"],
        ["Microsoft.UI.Xaml.Data.DataErrorsChangedEventArgs"] = ["PropertyName"],
        ["Microsoft.UI.Xaml.Interop.INotifyCollectionChanged"] = [],
        ["Microsoft.UI.Xaml.Interop.NotifyCollectionChangedAction"] = [],
        ["Microsoft.UI.Xaml.Interop.NotifyCollectionChangedEventArgs"] = ["Action", "NewItems", "NewStartingIndex", "OldItems", "OldStartingIndex"],
        ["Microsoft.UI.Xaml.Interop.NotifyCollectionChangedEventHandler"] = [],
        ["Microsoft.UI.Xaml.CornerRadiusHelper"] = [],
        ["Microsoft.UI.Xaml.DurationHelper"] = ["Automatic", "Forever"],
        ["Microsoft.UI.Xaml.GridLengthHelper"] = ["Auto"],
        ["Microsoft.UI.Xaml.ThicknessHelper"] = [],
        ["Microsoft.UI.Xaml.Controls.Primitives.GeneratorPositionHelper"] = [],
        ["Microsoft.UI.Xaml.Media.MatrixHelper"] = ["Identity"],
        ["Microsoft.UI.Xaml.Media.Animation.KeyTimeHelper"] = [],
        ["Microsoft.UI.Xaml.Media.Animation.RepeatBehaviorHelper"] = ["Forever"],
        ["Microsoft.UI.Xaml.Media.Media3D.Matrix3DHelper"] = ["Identity"],
    };

    /// <summary>
    /// Returns whether <paramref name="typeName"/> names a Windows Runtime type that the projection has no type for.
    /// </summary>
    public static bool IsTypeWithoutProjection(string typeName) => _TypesWithoutProjection.ContainsKey(typeName);

    /// <summary>
    /// Returns whether the metadata declares <paramref name="propertyName"/> for a type that the projection has no
    /// type for; see <see cref="IsTypeWithoutProjection"/>.
    /// </summary>
    public static bool DeclaresPropertyWithoutProjection(string typeName, string propertyName)
    {
        return _TypesWithoutProjection.TryGetValue(typeName, out string[]? properties)
            && Array.IndexOf(properties, propertyName) >= 0;
    }

    /// <summary>
    /// Returns whether <paramref name="type"/> declares a property the way Windows Runtime metadata records it:
    /// inherited properties belong to the base type, events are no properties, the members of a structure are
    /// fields, and a property counts whether it is public or protected.
    /// </summary>
    /// <param name="type">The projection of a Windows Runtime type.</param>
    /// <param name="propertyName">The name the metadata gives the property.</param>
    /// <param name="isWindowsRuntimeType">Returns whether a type or an interface is part of the Windows Runtime.</param>
    /// <remarks>
    /// The projection adds the members of .NET interfaces to a class that implements a Windows Runtime
    /// collection: <c>Count</c> and <c>IsReadOnly</c>, and an indexer. The metadata has none of them, and calls
    /// the count <c>Size</c>, which the projection does not have. The members of such an interface are left out,
    /// and <c>Size</c> is declared by a type that introduces a collection interface.
    /// </remarks>
    public static bool DeclaresProperty(Type type, string propertyName, Func<Type, bool> isWindowsRuntimeType)
    {
        // The members of a structure are fields in the metadata.
        if (type.IsValueType)
        {
            return false;
        }

        // Protected properties, such as Control.DefaultStyleKey, are part of the metadata as well. The private
        // members of the projection are not: the pointer to the native object, and the explicit
        // implementations of the interfaces that connect the object to the runtime.
        const BindingFlags declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        HashSet<MethodInfo> mapped = GetMembersOfMappedInterfaces(type, isWindowsRuntimeType);
        foreach (PropertyInfo property in type.GetProperties(declared))
        {
            if (property.Name == propertyName && property.GetAccessors(nonPublic: true).Any(accessor => IsVisibleToMetadata(accessor, mapped)))
            {
                return true;
            }
        }

        return propertyName == "Size" && IntroducesCollectionInterface(type);
    }

    private static bool IsVisibleToMetadata(MethodInfo accessor, HashSet<MethodInfo> mapped)
    {
        return (accessor.IsPublic || accessor.IsFamily || accessor.IsFamilyOrAssembly) && !mapped.Contains(accessor);
    }

    private static HashSet<MethodInfo> GetMembersOfMappedInterfaces(Type type, Func<Type, bool> isWindowsRuntimeType)
    {
        var members = new HashSet<MethodInfo>();

        // An interface has no implementations to map.
        if (type.IsInterface)
        {
            return members;
        }

        foreach (Type contract in type.GetInterfaces())
        {
            // An interface of the Windows Runtime keeps its members; a .NET interface is a mapping of one.
            if (isWindowsRuntimeType(contract))
            {
                continue;
            }

            try
            {
                members.UnionWith(type.GetInterfaceMap(contract).TargetMethods);
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
            {
                // The type has no map for the interface.
            }
        }

        return members;
    }

    // The count of a collection is Size in the metadata: IVector, IVectorView, IMap, IMapView and IBindableVector all declare it.
    private static bool IntroducesCollectionInterface(Type type)
    {
        IEnumerable<Type> inherited = type.BaseType?.GetInterfaces() ?? Type.EmptyTypes;
        return type.GetInterfaces().Except(inherited).Any(IsCollectionInterface);
    }

    private static bool IsCollectionInterface(Type contract)
    {
        if (contract == typeof(IList))
        {
            return true;
        }

        if (!contract.IsGenericType)
        {
            return false;
        }

        Type definition = contract.GetGenericTypeDefinition();
        return definition == typeof(IList<>)
            || definition == typeof(IReadOnlyList<>)
            || definition == typeof(IDictionary<,>)
            || definition == typeof(IReadOnlyDictionary<,>);
    }
}
