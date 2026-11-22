// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

    // The folder of the staged project files; the surface itself runs from a folder below it.
    private static string? _contentDirectory;

    // The folder of the files that the surface writes for itself, shared by all surface processes.
    private static readonly string _PlaceholderDirectory = Path.Combine(Path.GetTempPath(), "WinUIDesigner", "Surface");

    private static readonly List<string> _ProviderTypes = [];

    private static readonly List<IXamlMetadataProvider> _Providers = [];

    private static bool _providersInitialized;

    [ThreadStatic] private static HashSet<string>? _resolving;

    public static void Initialize(string directory)
    {
        _contentDirectory = directory;
        _PriFiles.AddRange(StagedResourceLookup.OrderPriFiles(directory, Directory.EnumerateFiles(directory, "*.pri", SearchOption.AllDirectories)));

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

                _Assemblies.TryAdd(assemblyName, file);

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
        var projects = new List<StagedPri>();

        foreach (string file in _PriFiles)
        {
            try
            {
                projects.Add(new StagedPri(file, new ResourceManager(file)));

            }
            catch (Exception ex)
            {
                WinUIDesignerLogger.LogWarning("Surface", $"Unable to load staged PRI '{file}'.", ex);
            }
        }

        // Each resource manager has a resolver of its own, with its own memory of what it has answered and reported.
        var resolver = new StagedResourceResolver<ResourceCandidate>(_contentDirectory, new StagedResourceHost(projects));
        primary.ResourceNotFound += (_, args) => ResolveStagedResource(args, resolver);

        return primary;
    }

    /// <summary>
    /// Answers a resource that the surface's own resource manager does not know: a file of the project, such as
    /// <c>ms-appx:///Assets/Images/logo.svg</c>, which MRT asks for as <c>Files/Assets/Images/logo.svg</c> and which a
    /// packaged application would find in its package.
    /// </summary>
    private static void ResolveStagedResource(ResourceNotFoundEventArgs args, StagedResourceResolver<ResourceCandidate> resolver)
    {
        string name = string.Empty;

        // MRT raises the event from native code, inside a lookup of WinUI: no exception may leave it.
        try
        {
            name = args.Name;
            KeyValuePair<string, string>[] qualifiers = args.Context is { } context
                ? [.. context.QualifierValues.Where(qualifier => !string.IsNullOrEmpty(qualifier.Value))]
                : [];

            if (resolver.Resolve(name, qualifiers) is { } candidate)
            {
                args.SetResolvedCandidate(candidate);
            }
        }
        catch (Exception ex)
        {
            resolver.ReportFailure(name, ex);
        }
    }

    // A staged PRI file and the resource manager that reads it.
    private sealed record StagedPri(string File, ResourceManager Manager);

    // The staged PRI files of the project through MRT Core, its staged files and the log of the surface.
    private sealed class StagedResourceHost(List<StagedPri> projects) : IStagedResourceHost<ResourceCandidate>
    {
        public IReadOnlyList<string> PriFiles { get; } = [.. projects.Select(project => project.File)];

        // The context of the request belongs to the surface's own resource manager and must not be passed to another
        // one: MRT Core then misses a resource with qualified candidates, such as back.png with its scale variants, and
        // fails with an access violation on one that has only theme variants. A context of the file's own manager with
        // the same values finds both.
        public ResourceCandidate? LookUp(int priFile, string name, IReadOnlyList<KeyValuePair<string, string>>? qualifiers)
        {
            ResourceManager manager = projects[priFile].Manager;

            // The defaults of the PRI file.
            if (qualifiers is null)
            {
                return manager.MainResourceMap.TryGetValue(name);
            }

            // The qualifier values of the request, such as scale and theme.
            ResourceContext context = manager.CreateResourceContext();
            foreach (KeyValuePair<string, string> qualifier in qualifiers)
            {
                context.QualifierValues[qualifier.Key] = qualifier.Value;
            }

            return manager.MainResourceMap.TryGetValue(name, context);
        }

        public string? GetFilePath(ResourceCandidate candidate)
        {
            return candidate.Kind == ResourceCandidateKind.FilePath ? candidate.ValueAsString : null;
        }

        public ResourceCandidate CreateFileCandidate(string path) => new(ResourceCandidateKind.FilePath, path);

        // "the file 'C:\...\back.scale-150.png' [Scale=150]" or "EmbeddedData []".
        public string Describe(ResourceCandidate candidate)
        {
            string value = candidate.Kind == ResourceCandidateKind.FilePath ? $"the file '{candidate.ValueAsString}'" : candidate.Kind.ToString();
            return $"{value} [{StagedResourceLookup.DescribeQualifiers(candidate.QualifierValues)}]";
        }

        public bool FileExists(string path) => File.Exists(path);

        public DateTime GetLastWriteTime(string path) => File.GetLastWriteTime(path);

        public string WritePlaceholder(string name) => MissingImagePlaceholder.Write(_PlaceholderDirectory, name);

        public void Log(StagedResourceLogLevel level, string message, Exception? exception)
        {
            switch (level)
            {
                // Only with verbose logging.
                case StagedResourceLogLevel.Trace:
                    WinUIDesignerLogger.LogTrace("Surface", message, exception);
                    break;

                // Always, such as XAML that comes from the build output.
                case StagedResourceLogLevel.Information:
                    WinUIDesignerLogger.LogInformation("Surface", message, exception);
                    break;

                // Always, such as a missing image.
                default:
                    WinUIDesignerLogger.LogWarning("Surface", message, exception);
                    break;
            }
        }
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
                if (provider.GetXamlType(name) is { } type) return type;
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

        return [.. _Providers.SelectMany(value => value.GetXmlnsDefinitions() ?? [])];
    }
}
