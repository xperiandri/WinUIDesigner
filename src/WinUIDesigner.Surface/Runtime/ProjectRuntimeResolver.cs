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

    // Names whose resolution is already in the log: WinUI asks for the same resources again and again.
    private static readonly HashSet<string> _ReportedResources = new(StringComparer.OrdinalIgnoreCase);

    // Names that no staged PRI file answers, neither for the qualifier values of a request nor for the defaults. The
    // PRI files are loaded once, when the surface starts.
    private static readonly HashSet<string> _NamesUnknownToStagedPris = new(StringComparer.OrdinalIgnoreCase);

    // The placeholder files of missing images by file name, each written once.
    private static readonly Dictionary<string, string> _Placeholders = new(StringComparer.OrdinalIgnoreCase);

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

        primary.ResourceNotFound += (_, args) => ResolveStagedResource(args, projects);

        return primary;
    }

    /// <summary>
    /// Answers a resource that the surface's own resource manager does not know: a file of the project, such as
    /// <c>ms-appx:///Assets/Images/logo.svg</c>, which MRT asks for as <c>Files/Assets/Images/logo.svg</c> and which a
    /// packaged application would find in its package.
    /// </summary>
    private static void ResolveStagedResource(ResourceNotFoundEventArgs args, List<StagedPri> projects)
    {
        string name = string.Empty;

        // MRT raises the event from native code, inside a lookup of WinUI: no exception may leave it.
        try
        {
            name = args.Name;
            KeyValuePair<string, string>[] qualifiers = args.Context is { } context
                ? [.. context.QualifierValues.Where(qualifier => !string.IsNullOrEmpty(qualifier.Value))]
                : [];

            // 1. A staged PRI file knows the resource.
            if (FindInStagedPris(projects, name, qualifiers) is { } found)
            {
                args.SetResolvedCandidate(found.Candidate);
                if (IsFirstReport(name))
                {
                    WinUIDesignerLogger.LogTrace("Surface", $"Resource '{name}' [{StagedResourceLookup.DescribeQualifiers(qualifiers)}] was found in the staged PRI '{found.PriFile}' as {Describe(found.Candidate)}.");
                }

                return;
            }

            // 2. The staged file at the exact path of the URI, such as an image in a folder named after a qualifier
            // (theme-dark/logo.svg), which no PRI file lists under that name, or the XBF of a library.
            if (_contentDirectory is { } root && StagedResourceLookup.FindStagedFile(root, name, File.Exists) is { } path)
            {
                args.SetResolvedCandidate(new ResourceCandidate(ResourceCandidateKind.FilePath, path));
                if (IsFirstReport(name))
                {
                    WinUIDesignerLogger.LogTrace("Surface", $"Resource '{name}' is in none of the {projects.Count} staged PRI files; the staged file '{path}' is used.");
                }

                return;
            }

            // 3. An image that is neither: an empty image, so that the document can still be drawn.
            if (MissingImagePlaceholder.IsImage(name))
            {
                args.SetResolvedCandidate(new ResourceCandidate(ResourceCandidateKind.FilePath, GetPlaceholder(name)));
                if (IsFirstReport(name))
                {
                    WinUIDesignerLogger.LogWarning("Surface", $"The image '{StagedResourceLookup.ToAppUri(name)}' is in none of the {projects.Count} staged PRI files and is not a staged file in '{_contentDirectory}'; the designer shows it empty.");
                }

                return;
            }

            // 4. Anything else stays unresolved, as before; the line shows what the framework asked for.
            if (IsFirstReport(name))
            {
                WinUIDesignerLogger.LogTrace("Surface", $"Resource '{name}' [{StagedResourceLookup.DescribeQualifiers(qualifiers)}] is in none of the {projects.Count} staged PRI files and is not a staged file; it stays unresolved.");
            }
        }
        catch (Exception ex)
        {
            if (IsFirstReport($"failed|{name}"))
            {
                WinUIDesignerLogger.LogWarning("Surface", $"Resolving the resource '{name}' failed; it stays unresolved.", ex);
            }
        }
    }

    // The first usable candidate of the staged PRI files, in their order.
    private static StagedCandidate? FindInStagedPris(List<StagedPri> projects, string name, KeyValuePair<string, string>[] qualifiers)
    {
        lock (_NamesUnknownToStagedPris)
        {
            if (_NamesUnknownToStagedPris.Contains(name))
            {
                return null;
            }
        }

        StagedCandidate? found = StagedResourceLookup.FirstUsable(
            GetStagedCandidates(projects, name, qualifiers),
            staged => staged.Candidate.Kind == ResourceCandidateKind.FilePath ? staged.Candidate.ValueAsString : null,
            File.Exists
        );

        if (found is null)
        {
            lock (_NamesUnknownToStagedPris)
            {
                _NamesUnknownToStagedPris.Add(name);
            }
        }

        return found;
    }

    // For each staged PRI file, the candidate for the qualifier values of the request (scale, theme, contrast), then the
    // one for the defaults. The context of the request belongs to the surface's own resource manager and must not be
    // passed to another one: MRT Core then misses a resource with qualified candidates, such as back.png with its
    // scale variants, and fails with an access violation on one that has only theme variants. A context of the file's
    // own manager with the same values finds both.
    private static IEnumerable<StagedCandidate?> GetStagedCandidates(List<StagedPri> projects, string name, KeyValuePair<string, string>[] qualifiers)
    {
        foreach (StagedPri project in projects)
        {
            if (qualifiers.Length > 0)
            {
                ResourceContext context = project.Manager.CreateResourceContext();
                foreach (KeyValuePair<string, string> qualifier in qualifiers)
                {
                    context.QualifierValues[qualifier.Key] = qualifier.Value;
                }

                yield return ToStagedCandidate(project, project.Manager.MainResourceMap.TryGetValue(name, context));
            }

            yield return ToStagedCandidate(project, project.Manager.MainResourceMap.TryGetValue(name));
        }
    }

    private static StagedCandidate? ToStagedCandidate(StagedPri project, ResourceCandidate? candidate)
    {
        return candidate is null ? null : new StagedCandidate(project.File, candidate);
    }

    // What a candidate is, for the log: "the file 'C:\...\back.scale-150.png' [Scale=150]" or "EmbeddedData []".
    private static string Describe(ResourceCandidate candidate)
    {
        string value = candidate.Kind == ResourceCandidateKind.FilePath ? $"the file '{candidate.ValueAsString}'" : candidate.Kind.ToString();
        return $"{value} [{StagedResourceLookup.DescribeQualifiers(candidate.QualifierValues)}]";
    }

    // The placeholder file for a missing image, written once per process.
    private static string GetPlaceholder(string name)
    {
        string fileName = MissingImagePlaceholder.GetFileName(name);
        lock (_Placeholders)
        {
            if (!_Placeholders.TryGetValue(fileName, out string? path))
            {
                path = MissingImagePlaceholder.Write(_PlaceholderDirectory, name);
                _Placeholders.Add(fileName, path);
            }

            return path;
        }
    }

    // Whether the resolution of a resource is written to the log for the first time.
    private static bool IsFirstReport(string key)
    {
        lock (_ReportedResources)
        {
            return _ReportedResources.Add(key);
        }
    }

    // A staged PRI file and the resource manager that reads it.
    private sealed record StagedPri(string File, ResourceManager Manager);

    // A candidate of a staged PRI file.
    private sealed record StagedCandidate(string PriFile, ResourceCandidate Candidate);

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
