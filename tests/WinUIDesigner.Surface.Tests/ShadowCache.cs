// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System.Xml.Linq;
using WinUIDesigner.Markup;
using WinUIDesigner.Surface.Markup;

/// <summary>
/// Visual Studio's shadow cache in memory, the WinUI runtime as a list of answers, and what the expander reported.
/// </summary>
internal sealed class ShadowCache
{
    public const string ContentDirectory = @"C:\DesignerCache\1-1868410451x64DA";
    public const string Contract = "Windows.Foundation.UniversalApiContract";

    private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Exception> _failures = new(StringComparer.OrdinalIgnoreCase);
    private readonly XamlConditionService _conditions = new(new XamlConditionProbeSet
    {
        IsApiContractPresent = (name, major, _) => name == Contract && major <= 10,
    });

    public ShadowCache(int maxDepth = ResourceDictionarySourceOptions.DefaultMaxDepth)
    {
        Expander = new ResourceDictionarySourceExpander(new ResourceDictionarySourceOptions
        {
            ContentDirectory = ContentDirectory,
            ReadFile = path => _failures.TryGetValue(path, out Exception? failure)
                ? throw failure
                : _files.TryGetValue(path, out string? text) ? text : null,
            Parse = (xaml, description) =>
            {
                Parsed.Add(description);
                XDocument document = XDocument.Parse(xaml, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
                XamlNamespaceRewriter.Rewrite(document, new XamlNamespaceRewriteOptions { SourceXaml = xaml, EvaluateCondition = _conditions.Evaluate });
                return document;
            },
            IsDictionaryType = name => DictionaryTypes.Contains($"{name.NamespaceName}:{name.LocalName}"),
            LoadWithRuntime = uri =>
            {
                RuntimeLoads.Add(uri);
                return RuntimeErrors.TryGetValue(uri, out string? error) ? error : null;
            },
            MaxDepth = maxDepth,
            ReportWarning = Warnings.Add,
            Report = Reports.Add,
        });
    }

    public ResourceDictionarySourceExpander Expander { get; }

    public List<string> Warnings { get; } = [];

    public List<string> Reports { get; } = [];

    public List<string> Parsed { get; } = [];

    public List<string> RuntimeLoads { get; } = [];

    public Dictionary<string, string> RuntimeErrors { get; } = new(StringComparer.Ordinal);

    public HashSet<string> DictionaryTypes { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Writes the prepared copy of the dictionary <paramref name="uri"/> names, as Visual Studio does.
    /// </summary>
    public void Add(string uri, string xaml) => _files[ResourceDictionarySourceUri.TryGetLocalPath(uri, ContentDirectory)!] = xaml;

    /// <summary>
    /// Writes the copy of the file of the dictionary <paramref name="uri"/> names that the build laid out, beside
    /// the prepared copy and without the marker in its name.
    /// </summary>
    public void AddBuilt(string uri, string xaml)
    {
        string prepared = ResourceDictionarySourceUri.TryGetLocalPath(uri, ContentDirectory)!;
        _files[ResourceDictionarySourceUri.TryGetBuildLayoutPath(prepared)!] = xaml;
    }

    /// <summary>
    /// Makes reading the copy of the file of the dictionary <paramref name="uri"/> names that the build laid out
    /// throw <paramref name="failure"/>.
    /// </summary>
    public void FailBuilt(string uri, Exception failure)
    {
        string prepared = ResourceDictionarySourceUri.TryGetLocalPath(uri, ContentDirectory)!;
        _failures[ResourceDictionarySourceUri.TryGetBuildLayoutPath(prepared)!] = failure;
    }

    /// <summary>
    /// Returns a prepared dictionary with <paramref name="body"/> in it, as Visual Studio 18 writes it: the prefix of
    /// its own design-time properties is <c>_0</c>.
    /// </summary>
    public static string Dictionary(string body)
    {
        return $"""<ResourceDictionary xmlns:_0="using:XSurfUwp" xmlns="{XamlNamespaces.Presentation}" xmlns:x="{XamlNamespaces.XamlLanguage}">{body}</ResourceDictionary>""";
    }

    /// <summary>
    /// Returns a dictionary element whose source Visual Studio's cleaner replaced with the design-time property.
    /// </summary>
    public static string Merge(string uri) => $"""<ResourceDictionary _0:DT.ResourceDictionarySource="{uri}" />""";

    /// <summary>
    /// A page as Visual Studio prepares it, with <paramref name="resources"/> inside the dictionary of its resources.
    /// </summary>
    public static XElement Page(string resources)
    {
        return XElement.Parse($"""
            <Page xmlns:_0="using:XSurfUwp" xmlns="{XamlNamespaces.Presentation}" xmlns:x="{XamlNamespaces.XamlLanguage}">
              <Page.Resources>
                <ResourceDictionary>{resources}</ResourceDictionary>
              </Page.Resources>
              <Border />
            </Page>
            """, LoadOptions.PreserveWhitespace);
    }

    /// <summary>
    /// Returns the <c>x:Key</c> of an element.
    /// </summary>
    public static string? KeyOf(XElement element) => (string?)element.Attribute(XName.Get("Key", XamlNamespaces.XamlLanguage));
}
