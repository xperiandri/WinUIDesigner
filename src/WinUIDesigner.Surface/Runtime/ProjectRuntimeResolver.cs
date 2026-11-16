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
    private static readonly HashSet<string> _ReportedFailures = new(StringComparer.Ordinal);

    [ThreadStatic] private static HashSet<string>? _resolving;

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
            return name.Name is string simpleName && _Assemblies.TryGetValue(simpleName, out string? file)
                ? AssemblyLoadContext.Default.LoadFromAssemblyPath(file)
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
                    ReportFailureOnce($"type|{provider.GetType().FullName}|{ex.GetType().FullName}|{ex.Message}", $"The XAML metadata provider '{provider.GetType().FullName}' of the project failed to describe the type '{name}'.", ex);
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
                ReportFailureOnce($"xmlns|{provider.GetType().FullName}|{ex.GetType().FullName}|{ex.Message}", $"The XAML metadata provider '{provider.GetType().FullName}' of the project failed to list its XML namespaces.", ex);
            }
        }

        return [.. definitions];
    }

    private static void ReportFailureOnce(string key, string message, Exception exception)
    {
        lock (_ReportedFailures)
        {
            if (!_ReportedFailures.Add(key))
            {
                return;
            }
        }

        WinUIDesignerLogger.LogWarning("Surface", message, exception);
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
    /// assembly cannot satisfy. Loading such an assembly fails, and a failure inside XAML ends the process without a
    /// word in the log; this names the cause at startup.
    /// </summary>
    private static void ReportUnsatisfiedReferences(List<(string Assembly, List<AssemblyReferenceInfo> References)> staged)
    {
        try
        {
            // The assemblies the surface itself carries, by simple name.
            var surfaceAssemblies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string trusted)
            {
                foreach (string path in trusted.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                {
                    surfaceAssemblies.TryAdd(Path.GetFileNameWithoutExtension(path), path);
                }
            }

            var versions = new Dictionary<string, Version?>(StringComparer.OrdinalIgnoreCase);
            Version? GetSurfaceVersion(string name)
            {
                if (!versions.TryGetValue(name, out Version? version))
                {
                    version = null;
                    if (surfaceAssemblies.TryGetValue(name, out string? path))
                    {
                        try
                        {
                            version = AssemblyName.GetAssemblyName(path).Version;
                        }
                        catch (Exception ex) when (ex is IOException or BadImageFormatException)
                        {
                            // Not a managed assembly, or not readable: it cannot be compared.
                        }
                    }

                    versions[name] = version;
                }

                return version;
            }

            var findings = new List<UnsatisfiedAssemblyReference>();
            foreach ((string assembly, List<AssemblyReferenceInfo> references) in staged)
            {
                findings.AddRange(AssemblyReferenceCompatibility.FindUnsatisfied(assembly, references, GetSurfaceVersion));
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
}
