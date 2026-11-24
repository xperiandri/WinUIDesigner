// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace WinUIDesigner.Surface.Runtime;

/// <summary>
/// Creates the design-time data context that <c>d:DesignInstance</c> describes, without ever throwing.
/// </summary>
/// <remarks>
/// Visual Studio sends the type of the instance as its assembly-qualified name. The data context is created only when
/// the author allows the designer to run the type's code (<c>IsDesignTimeCreatable</c>), and only through a public
/// parameterless constructor; in every other case the data context stays empty.
/// </remarks>
internal static class DesignInstanceFactory
{
    /// <summary>
    /// Creates an instance of <paramref name="type"/>, or a list that holds one instance when
    /// <paramref name="createList"/> is set.
    /// </summary>
    /// <param name="type">The type: the assembly-qualified name that Visual Studio sends, or a <see cref="Type"/>.</param>
    /// <param name="isDesignTimeCreatable">Whether the author allows the designer to create the type.</param>
    /// <param name="createList">Whether the data context is a list that holds the instance.</param>
    /// <param name="resolveType">Resolves an assembly-qualified type name, or returns <see langword="null"/>.</param>
    /// <param name="failure">
    /// The exception that the resolution of the type or its constructor threw, without the wrapper of reflection;
    /// <see langword="null"/> when nothing threw.
    /// </param>
    /// <returns>The instance, the list, or <see langword="null"/> when nothing is created.</returns>
    public static object? Create(object? type, bool isDesignTimeCreatable, bool createList, Func<string, Type?> resolveType, out Exception? failure)
    {
        failure = null;

        // The author did not allow the designer to run the code of the type.
        if (!isDesignTimeCreatable)
        {
            return null;
        }

        try
        {
            Type? instanceType = type switch
            {
                // A type, if a builder sends one.
                Type value => value,
                // The assembly-qualified name that Visual Studio sends.
                string name when !string.IsNullOrWhiteSpace(name) => resolveType(name),
                // No type at all.
                _ => null,
            };

            // A type that does not resolve, or that cannot be created through a public parameterless constructor.
            if (instanceType is null
                || instanceType.IsAbstract
                || instanceType.IsInterface
                || instanceType.ContainsGenericParameters
                || (!instanceType.IsValueType && instanceType.GetConstructor(Type.EmptyTypes) is null))
            {
                return null;
            }

            object? instance = Activator.CreateInstance(instanceType);
            if (!createList)
            {
                return instance;
            }

            // A list of the type, for a collection that an items control binds to.
            var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(instanceType))!;
            list.Add(instance);
            return list;
        }
        catch (Exception ex)
        {
            // The code of the project failed: its constructor, or the loading of its assembly.
            failure = ex is TargetInvocationException { InnerException: { } inner } ? inner : ex;
            return null;
        }
    }
}
