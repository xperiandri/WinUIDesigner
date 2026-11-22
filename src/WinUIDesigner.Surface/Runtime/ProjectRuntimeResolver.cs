// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using System.Runtime.InteropServices;
using Microsoft.Windows.ApplicationModel.Resources;

namespace WinUIDesigner.Surface.Runtime;

/// <summary>
/// Resolves staged project assemblies, XAML metadata, and resources in the surface process.
/// Indexes staged metadata without loading every project dependency into the Visual Studio host.
/// </summary>
internal static class ProjectRuntimeResolver
{
    private static readonly Dictionary<string, string> _Assemblies = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, string> _Types = new(StringComparer.Ordinal);

    private static readonly Dictionary<string, string> _NativeLibraries = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<string> _PriFiles = [];

    private static readonly List<string> _ProviderTypes = [];

    private static readonly List<IXamlMetadataProvider> _Providers = [];

    private static bool _providersInitialized;

    // Failures of the project's metadata providers already written to the log; XAML asks the same questions again and again.
    private static readonly ReportedFailures _ReportedFailures = new();

    // The assemblies the surface carries itself.
    private static readonly SurfaceAssemblies _SurfaceAssemblies = new(AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string, ReadAssemblyVersion);

    // The simple names of the references that the surface's own, older copy has served; each is written to the log once.
    private static readonly HashSet<string> _ServedReferences = new(StringComparer.OrdinalIgnoreCase);

    [ThreadStatic] private static HashSet<string>? _resolving;

    /// <summary>
    /// Lets the surface's own copy of a WinRT projection serve a project's reference to a newer version of it, which
    /// the runtime does not bind to that copy by itself (<see cref="AssemblyReferenceCompatibility.Resolve"/>).
    /// </summary>
    /// <remarks>
    /// Call it before any other handler of <see cref="AssemblyLoadContext.Resolving"/> is added. Visual Studio's handler
    /// would otherwise try a file of the same name from Visual Studio's folders first, and the runtime refuses to load
    /// a copy that is newer than the surface's.
    /// </remarks>
    public static void ServeNewerProjectionReferences()
    {
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            Assembly? surfaceCopy = AssemblyReferenceCompatibility.ServeFromSurfaceCopy(context, name, _SurfaceAssemblies);

            // A newer reference to a projection that the surface carries, served by its own copy.
            if (surfaceCopy is not null)
            {
                ReportServedOnce(name, surfaceCopy);
            }

            return surfaceCopy;
        };
    }

    public static void Initialize(string directory)
    {
        _PriFiles.AddRange(Directory.EnumerateFiles(directory, "*.pri", SearchOption.AllDirectories));

        var references = new List<(string Assembly, List<AssemblyReferenceInfo> References)>();

        foreach (string file in Directory.EnumerateFiles(directory, "*.dll", SearchOption.AllDirectories)
            .OrderBy(file => Path.GetRelativePath(directory, file).Count(character => character == Path.DirectorySeparatorChar))
            .ThenBy(file => file, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                using var stream = File.OpenRead(file);
                using var pe = new PEReader(stream);

                if (!pe.HasMetadata)
                {
                    if (pe.PEHeaders.CoffHeader.Machine == Machine.Amd64)
                    {
                        _NativeLibraries.TryAdd(Path.GetFileNameWithoutExtension(file), file);
                    }

                    continue;
                }

                MetadataReader metadata = pe.GetMetadataReader();

                if (!metadata.IsAssembly)
                {
                    continue;
                }

                string assemblyName = metadata.GetString(metadata.GetAssemblyDefinition().Name);
                if (assemblyName.StartsWith("System.", StringComparison.Ordinal) ||
                    assemblyName.StartsWith("Microsoft.VisualStudio.", StringComparison.Ordinal) ||
                    assemblyName == "WinUISurface" || assemblyName == "Microsoft.WinUI")
                {
                    continue;
                }

                // The project's copy of an assembly the surface carries is never loaded, neither for a reference nor for a
                // type: the runtime binds only the surface's copy, and cannot load a newer one beside it.
                if (_SurfaceAssemblies.Contains(assemblyName))
                {
                    continue;
                }

                if (_Assemblies.TryAdd(assemblyName, file))
                {
                    references.Add((assemblyName, ReadReferences(metadata)));
                }

                foreach (TypeDefinitionHandle handle in metadata.TypeDefinitions)
                {
                    var definition = metadata.GetTypeDefinition(handle);
                    var name = metadata.GetString(definition.Name);
                    var ns = metadata.GetString(definition.Namespace);

                    if (string.IsNullOrEmpty(ns))
                    {
                        continue;
                    }

                    var fullName = ns + "." + name;
                    _Types.TryAdd(fullName, assemblyName);

                    foreach (InterfaceImplementationHandle implementation in definition.GetInterfaceImplementations())
                    {
                        EntityHandle type = metadata.GetInterfaceImplementation(implementation).Interface;

                        if (type.Kind == HandleKind.TypeReference &&
                            metadata.GetString(metadata.GetTypeReference((TypeReferenceHandle)type).Name) == nameof(IXamlMetadataProvider))
                        {
                            _ProviderTypes.Add(fullName);
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or BadImageFormatException)
            {
                WinUIDesignerLogger.LogWarning("Surface", $"Unable to index staged assembly '{file}'.", ex);
            }
        }

        ReportUnsatisfiedReferences(references);

        AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            return AssemblyReferenceCompatibility.Resolve(name, _SurfaceAssemblies, _Assemblies.ContainsKey) == ReferenceResolution.ProjectCopy
                ? AssemblyLoadContext.Default.LoadFromAssemblyPath(_Assemblies[name.Name!])
                : null;
        };

        AssemblyLoadContext.Default.ResolvingUnmanagedDll += (_, name) =>
        {
            return _NativeLibraries.TryGetValue(Path.GetFileNameWithoutExtension(name), out var file)
                ? NativeLibrary.Load(file)
                : 0;
        };
    }

    public static IResourceManager CreateResourceManager()
    {
        var primary = new ResourceManager();
        var projects = new List<ResourceManager>();

        foreach (string file in _PriFiles)
        {
            try
            {
                projects.Add(new ResourceManager(file));

            }
            catch (Exception ex)
            {
                WinUIDesignerLogger.LogWarning("Surface", $"Unable to load staged PRI '{file}'.", ex);
            }
        }

        primary.ResourceNotFound += (_, args) =>
        {
            foreach (ResourceManager manager in projects)
            {
                var candidate = manager.MainResourceMap.TryGetValue(args.Name, args.Context);
                if (candidate is null)
                {
                    continue;
                }

                args.SetResolvedCandidate(candidate);

                return;
            }
        };

        return primary;
    }

    public static Type? ResolveType(string fullName)
    {
        return _Types.TryGetValue(fullName, out string? name) && _Assemblies.TryGetValue(name, out string? file)
            ? AssemblyLoadContext.Default.LoadFromAssemblyPath(file).GetType(fullName)
            : null;
    }

    private static void EnsureProviders()
    {
        if (_providersInitialized)
        {
            return;
        }

        _providersInitialized = true;

        foreach (string name in _ProviderTypes.Distinct())
        {
            // XAML asks for these providers from inside its own code. An exception that leaves here, such as the
            // FileLoadException of an assembly that was compiled against a newer version of an assembly than the
            // surface carries, is not reported to anyone: the process ends. One provider that cannot be created
            // must not take the others, or the designer, with it.
            try
            {
                var type = ResolveType(name);
                // A generated metadata provider is safe to construct independently;
                // constructing a second Application would replace the designer process.

                if (type is null || type.IsAbstract || typeof(Application).IsAssignableFrom(type))
                {
                    continue;
                }

                if (Activator.CreateInstance(type) is IXamlMetadataProvider provider)
                {
                    _Providers.Add(provider);
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                ReportFailureOnce($"create|{name}", $"The XAML metadata provider '{name}' of the project cannot be created, so the types it describes are not available to the designer.", ex);
            }
        }
    }

    public static IXamlType? GetXamlType(string name)
    {
        _resolving ??= new(StringComparer.Ordinal);
        if (!_resolving.Add(name))
        {
            return null;
        }

        try
        {
            EnsureProviders();

            foreach (IXamlMetadataProvider provider in _Providers)
            {
                try
                {
                    if (provider.GetXamlType(name) is { } type) return type;
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    ReportFailureOnce($"type|{provider.GetType().FullName}", $"The XAML metadata provider '{provider.GetType().FullName}' of the project failed to describe the type '{name}', and the designer cannot use the types that it fails to describe.", ex);
                }
            }

            return null;
        }
        finally
        {
            _resolving.Remove(name);
        }
    }

    public static XmlnsDefinition[] GetXmlnsDefinitions()
    {
        EnsureProviders();

        var definitions = new List<XmlnsDefinition>();
        foreach (IXamlMetadataProvider provider in _Providers)
        {
            try
            {
                definitions.AddRange(provider.GetXmlnsDefinitions() ?? []);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                ReportFailureOnce($"xmlns|{provider.GetType().FullName}", $"The XAML metadata provider '{provider.GetType().FullName}' of the project failed to list its XML namespaces, so the designer does not know them.", ex);
            }
        }

        return [.. definitions];
    }

    // A failure is known by what failed (the situation and the provider) and by its cause: the type, the HRESULT and the
    // file name of the exception (AssemblyLoadFailures.GetCauseKey). Not by its message, which is empty in the
    // single-file surface, and not by the type XAML asked for: a provider that cannot load an assembly fails in the
    // same way for every type that needs it.
    private static void ReportFailureOnce(string situation, string message, Exception exception)
    {
        if (!_ReportedFailures.TryAdd(situation, exception))
        {
            return;
        }

        WinUIDesignerLogger.LogWarning("Surface", $"{message} {AssemblyLoadFailures.DescribeCause(exception)} Later failures with the same cause are not logged.", exception);
    }

    private static void ReportServedOnce(AssemblyName reference, Assembly surfaceCopy)
    {
        lock (_ServedReferences)
        {
            if (!_ServedReferences.Add(reference.Name!))
            {
                return;
            }
        }

        WinUIDesignerLogger.LogInformation("Surface", $"{reference.Name} {reference.Version} is served by the design surface's own copy, {surfaceCopy.GetName().Version}; code that uses a type or member that only the newer version has fails.");
    }

    private static List<AssemblyReferenceInfo> ReadReferences(MetadataReader metadata)
    {
        var references = new List<AssemblyReferenceInfo>(metadata.AssemblyReferences.Count);
        foreach (AssemblyReferenceHandle handle in metadata.AssemblyReferences)
        {
            AssemblyReference reference = metadata.GetAssemblyReference(handle);
            references.Add(new AssemblyReferenceInfo(metadata.GetString(reference.Name), reference.Version));
        }

        return references;
    }

    /// <summary>
    /// Writes a warning for each reference of a staged assembly that the surface's own, older copy of the referenced
    /// assembly does not satisfy. Code that needs such a reference fails unless that copy serves it
    /// (<see cref="ServeNewerProjectionReferences"/>), and a failure inside XAML can end the process without a word in
    /// the log; this names the cause at startup.
    /// </summary>
    private static void ReportUnsatisfiedReferences(List<(string Assembly, List<AssemblyReferenceInfo> References)> staged)
    {
        try
        {
            var findings = new List<UnsatisfiedAssemblyReference>();
            foreach ((string assembly, List<AssemblyReferenceInfo> references) in staged)
            {
                findings.AddRange(AssemblyReferenceCompatibility.FindUnsatisfied(assembly, references, _SurfaceAssemblies.GetVersion));
            }

            foreach (string line in AssemblyReferenceCompatibility.Describe(findings))
            {
                WinUIDesignerLogger.LogWarning("Surface", line);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            WinUIDesignerLogger.LogTrace("Surface", "Checking the assembly references of the project failed.", ex);
        }
    }

    private static Version? ReadAssemblyVersion(string path)
    {
        try
        {
            return AssemblyName.GetAssemblyName(path).Version;
        }
        catch (Exception ex) when (ex is IOException or BadImageFormatException)
        {
            // Not a managed assembly, or not readable: it cannot be compared.
            return null;
        }
    }
}
