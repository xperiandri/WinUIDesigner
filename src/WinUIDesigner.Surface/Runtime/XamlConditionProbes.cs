// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Microsoft.UI.Xaml.Markup;
using Windows.Foundation.Metadata;
using WinUIDesigner.Markup;

namespace WinUIDesigner.Surface.Runtime;

/// <summary>
/// Answers, in the surface process, what conditional XAML asks about the running system.
/// </summary>
/// <remarks>
/// <para>
/// The XAML reader answers type and property conditions from Windows Runtime metadata: that of the operating
/// system for <c>Windows.*</c> names, and the <c>.winmd</c> files beside the executable for everything else.
/// The surface is a single-file executable without such files beside it, so there the reader finds no
/// <c>Microsoft.UI.Xaml</c> type at all. The probes look at the projection assemblies instead and apply the
/// rules of the metadata, measured against the reader in an application with Windows App SDK 2.5.1: a type is
/// present when it is a Windows Runtime type, and a property is present on the type that declares it, not on
/// derived types. A type that is only managed code, such as a class of the previewed project, is not present.
/// Where the projection differs from the metadata, <see cref="ProjectionMetadataRules"/> closes the gap.
/// </para>
/// <para>
/// The methods are safe to call from any thread, except <see cref="EvaluateCustom"/>.
/// </para>
/// </remarks>
internal static class XamlConditionProbes
{
    private const string _ProjectionAssemblySuffix = ".Projection";

    /// <summary>
    /// Creates the probes of the surface process.
    /// </summary>
    public static XamlConditionProbeSet Create()
    {
        return new XamlConditionProbeSet
        {
            IsApiContractPresent = IsApiContractPresent,
            IsWindowsTypePresent = ApiInformation.IsTypePresent,
            IsWindowsPropertyPresent = ApiInformation.IsPropertyPresent,
            IsManagedTypePresent = IsManagedTypePresent,
            IsManagedPropertyPresent = IsManagedPropertyPresent,
            EvaluateCustom = EvaluateCustom,
        };
    }

    /// <summary>
    /// Returns whether the operating system implements an API contract in at least the given version.
    /// </summary>
    public static bool IsApiContractPresent(string contractName, int majorVersion, int? minorVersion)
    {
        return minorVersion.HasValue
            ? ApiInformation.IsApiContractPresent(contractName, (ushort)majorVersion, (ushort)minorVersion.Value)
            : ApiInformation.IsApiContractPresent(contractName, (ushort)majorVersion);
    }

    /// <summary>
    /// Returns whether a Windows Runtime type that is not part of the operating system is present.
    /// </summary>
    public static bool IsManagedTypePresent(string typeName)
    {
        // Metadata that can be read is the authority, as it is for the XAML reader.
        return ApiInformation.IsTypePresent(typeName)
            || ProjectionMetadataRules.IsTypeWithoutProjection(typeName)
            || FindProjectedType(typeName) is not null;
    }

    /// <summary>
    /// Returns whether a Windows Runtime type that is not part of the operating system declares a property.
    /// </summary>
    public static bool IsManagedPropertyPresent(string typeName, string propertyName)
    {
        if (ApiInformation.IsPropertyPresent(typeName, propertyName))
        {
            return true;
        }

        // The projection has no type for some types of the metadata.
        if (ProjectionMetadataRules.IsTypeWithoutProjection(typeName))
        {
            return ProjectionMetadataRules.DeclaresPropertyWithoutProjection(typeName, propertyName);
        }

        Type? type = FindProjectedType(typeName);
        return type is not null && ProjectionMetadataRules.DeclaresProperty(type, propertyName, IsWindowsRuntimeType);
    }

    /// <summary>
    /// Creates a custom condition and evaluates it. Conditions are objects of the previewed application and
    /// usually derive from <see cref="Microsoft.UI.Xaml.DependencyObject"/>, so this has to run on the UI thread.
    /// </summary>
    /// <param name="typeName">The full name of a type that implements <see cref="IXamlCondition"/>.</param>
    /// <param name="argument">The text between the parentheses of the condition, exactly as written.</param>
    /// <exception cref="Exception">The type cannot be found, is not a condition, or fails.</exception>
    public static bool? EvaluateCustom(string typeName, string argument)
    {
        // No assembly that can be loaded here defines the type.
        Type type = FindType(typeName, loadProjections: false)
            ?? throw new TypeLoadException($"The type '{typeName}' was not found. Build the project so that the designer can load its assembly.");

        // The type exists and is no condition.
        if (!typeof(IXamlCondition).IsAssignableFrom(type))
        {
            throw new InvalidCastException($"The type '{typeName}' does not implement {typeof(IXamlCondition).FullName}.");
        }

        try
        {
            // The XAML reader creates one object per evaluation as well.
            var condition = (IXamlCondition)Activator.CreateInstance(type)!;
            return condition.Evaluate(argument);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            // Report what the constructor of the condition threw, not that it was called through reflection.
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    /// <summary>
    /// Finds the projection of a Windows Runtime type. A type that exists as managed code only is not one.
    /// </summary>
    internal static Type? FindProjectedType(string typeName)
    {
        Type? type = FindType(typeName, loadProjections: true);
        return type is not null && IsWindowsRuntimeType(type) ? type : null;
    }

    private static bool IsWindowsRuntimeType(Type type) => type.IsDefined(typeof(WinRT.WindowsRuntimeTypeAttribute), inherit: false);

    /// <summary>
    /// Finds a type by its full name: among the loaded assemblies, in the staged project, and optionally in
    /// projection assemblies of the Windows App SDK that are not loaded yet.
    /// </summary>
    /// <remarks>
    /// The name is taken as written. <see cref="WinUIDesigner.Surface.Common.XamlRuntimeUtilities.ResolveType(string)"/> is of no use here: it turns
    /// <c>Windows.UI.Xaml</c> names into <c>Microsoft.UI.Xaml</c> names, which are different types to a condition.
    /// </remarks>
    private static Type? FindType(string typeName, bool loadProjections)
    {
        // A name the runtime resolves by itself.
        Type? type = Type.GetType(typeName, throwOnError: false);
        if (type is not null)
        {
            return type;
        }

        // A type of an assembly that is loaded already.
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            type = assembly.GetType(typeName, throwOnError: false, ignoreCase: false);
            if (type is not null)
            {
                return type;
            }
        }

        // A type of the staged project.
        try
        {
            type = ProjectRuntimeResolver.ResolveType(typeName);
            if (type is not null)
            {
                return type;
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The staged assembly that defines the type cannot be loaded: the type is not available here.
        }

        // Only a Windows Runtime type is looked for in projection assemblies that are not loaded yet.
        if (!loadProjections)
        {
            return null;
        }

        foreach (string assemblyName in GetProjectionAssemblyNames(typeName))
        {
            try
            {
                type = Assembly.Load(new AssemblyName(assemblyName)).GetType(typeName, throwOnError: false, ignoreCase: false);
                if (type is not null)
                {
                    return type;
                }
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // No such assembly ships with the surface.
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the names of the Windows App SDK assemblies that could project <paramref name="typeName"/>.
    /// </summary>
    private static IEnumerable<string> GetProjectionAssemblyNames(string typeName)
    {
        // XAML, composition, input and windowing types live in two assemblies that are not named after a namespace.
        if (typeName.StartsWith("Microsoft.UI.", StringComparison.Ordinal))
        {
            yield return "Microsoft.WinUI";
            yield return "Microsoft.InteractiveExperiences.Projection";
        }

        // The others are named after the namespace they project: Microsoft.Windows.Storage.Pickers.Projection.
        for (int separator = typeName.LastIndexOf('.'); separator > 0; separator = typeName.LastIndexOf('.', separator - 1))
        {
            string namespaceName = typeName.Substring(0, separator);
            if (namespaceName.IndexOf('.') < 0)
            {
                // A single name such as "Microsoft" is not the namespace of an assembly.
                yield break;
            }

            yield return $"{namespaceName}{_ProjectionAssemblySuffix}";
        }
    }
}
