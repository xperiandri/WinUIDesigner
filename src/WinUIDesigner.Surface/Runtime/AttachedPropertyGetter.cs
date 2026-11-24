// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace WinUIDesigner.Surface.Runtime;

/// <summary>
/// Reads an attached property through the static <c>Get</c> accessor of the type that declares it, the way the XAML
/// parser reads an attached collection before it adds items to it.
/// </summary>
/// <remarks>
/// Some attached collections are created by their accessor on first use: <c>Interaction.Behaviors</c> of
/// Microsoft.Xaml.Interactivity has the default value <see langword="null"/>, and <c>Interaction.GetBehaviors</c>
/// creates the collection and stores it on the element.
/// </remarks>
internal static class AttachedPropertyGetter
{
    /// <summary>
    /// Finds the public static method <c>Get{propertyName}</c> of <paramref name="declaringType"/> that returns a value
    /// and takes one argument that accepts <paramref name="targetType"/>.
    /// </summary>
    /// <returns>The accessor for the most specific argument type, or <see langword="null"/> when there is none.</returns>
    public static MethodInfo? Find(Type declaringType, ReadOnlySpan<char> propertyName, Type targetType)
    {
        string accessorName = $"Get{propertyName}";
        MethodInfo? accessor = null;
        foreach (MethodInfo method in declaringType.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy))
        {
            // Another method, or one that cannot be the getter of a value.
            if (method.Name != accessorName || method.ReturnType == typeof(void) || method.ContainsGenericParameters)
            {
                continue;
            }

            // An accessor for owners of another kind than the target.
            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length != 1 || !parameters[0].ParameterType.IsAssignableFrom(targetType))
            {
                continue;
            }

            // Of two overloads that both accept the target, the one for the more specific owner wins.
            if (accessor is null || accessor.GetParameters()[0].ParameterType.IsAssignableFrom(parameters[0].ParameterType))
            {
                accessor = method;
            }
        }

        return accessor;
    }

    /// <summary>
    /// Reads the attached property <paramref name="propertyName"/> of <paramref name="target"/> through the static
    /// accessor that <paramref name="declaringType"/> defines for it.
    /// </summary>
    /// <returns><see langword="false"/> when <paramref name="declaringType"/> defines no such accessor.</returns>
    public static bool TryGetValue(Type declaringType, ReadOnlySpan<char> propertyName, object target, out object? value)
    {
        MethodInfo? accessor = Find(declaringType, propertyName, target.GetType());
        if (accessor is null)
        {
            value = null;
            return false;
        }

        try
        {
            value = accessor.Invoke(null, [target]);
            return true;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            // The exception of the accessor itself, not the wrapper of reflection.
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }
}
