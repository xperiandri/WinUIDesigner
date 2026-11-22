// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;

namespace WinUIDesigner.Surface.Runtime;

/// <summary>
/// The level of a line that <see cref="StagedResourceResolver{TCandidate}"/> writes to the log.
/// </summary>
internal enum StagedResourceLogLevel
{
    /// <summary>
    /// Verbose diagnostics: what the framework asked for and what answered it.
    /// </summary>
    Trace,

    /// <summary>
    /// What the user should know although nothing failed, such as XAML of a library that comes from the build output.
    /// </summary>
    Information,

    /// <summary>
    /// What the designer cannot show as it is, such as a missing image.
    /// </summary>
    Warning,
}

/// <summary>
/// What <see cref="StagedResourceResolver{TCandidate}"/> needs from the surface: the staged PRI files, the file system,
/// the placeholder images and the log. The surface gives MRT Core and the real files, a test gives fakes.
/// </summary>
/// <typeparam name="TCandidate">What a resource resolves to; <c>ResourceCandidate</c> in the surface.</typeparam>
internal interface IStagedResourceHost<TCandidate>
    where TCandidate : class
{
    /// <summary>
    /// The staged PRI files that are loaded, in the order in which they are asked.
    /// </summary>
    IReadOnlyList<string> PriFiles { get; }

    /// <summary>
    /// The candidate that the PRI file at <paramref name="priFile"/> in <see cref="PriFiles"/> has for
    /// <paramref name="name"/>: for the qualifier values <paramref name="qualifiers"/>, or for the defaults of the PRI
    /// file when they are <see langword="null"/>. <see langword="null"/> when the PRI file does not know the name.
    /// </summary>
    TCandidate? LookUp(int priFile, string name, IReadOnlyList<KeyValuePair<string, string>>? qualifiers);

    /// <summary>
    /// The file that <paramref name="candidate"/> names, or <see langword="null"/> for data, such as an XBF that a PRI
    /// file embeds.
    /// </summary>
    string? GetFilePath(TCandidate candidate);

    /// <summary>
    /// A candidate that names the file <paramref name="path"/>.
    /// </summary>
    TCandidate CreateFileCandidate(string path);

    /// <summary>
    /// What <paramref name="candidate"/> is, for the log.
    /// </summary>
    string Describe(TCandidate candidate);

    /// <summary>
    /// Whether the file <paramref name="path"/> exists.
    /// </summary>
    bool FileExists(string path);

    /// <summary>
    /// When the file <paramref name="path"/> was written last, in local time.
    /// </summary>
    DateTime GetLastWriteTime(string path);

    /// <summary>
    /// Writes the placeholder for the image <paramref name="name"/> and returns its path.
    /// </summary>
    string WritePlaceholder(string name);

    /// <summary>
    /// Writes <paramref name="message"/> to the log.
    /// </summary>
    void Log(StagedResourceLogLevel level, string message, Exception? exception = null);
}

/// <summary>
/// Answers a resource that the surface's own resource manager does not know, such as the file
/// <c>ms-appx:///Assets/Images/logo.svg</c> of the project, which MRT asks for as <c>Files/Assets/Images/logo.svg</c>
/// and which a packaged application would find in its package. The answer is, in this order, the candidate of a staged
/// PRI file, the staged file at the path of the URI, an empty image for an image, or nothing. Each outcome is written to
/// the log once per name. The members can be called from any thread.
/// </summary>
/// <typeparam name="TCandidate">What a resource resolves to; <c>ResourceCandidate</c> in the surface.</typeparam>
internal sealed class StagedResourceResolver<TCandidate>(string? contentDirectory, IStagedResourceHost<TCandidate> host)
    where TCandidate : class
{
    // Names that no staged PRI file answers, neither for the qualifier values of a request nor for the defaults. The PRI
    // files are loaded once, when the surface starts.
    private readonly HashSet<string> _unknownNames = new(StringComparer.OrdinalIgnoreCase);

    // The placeholder files by file name, each written once.
    private readonly Dictionary<string, string> _placeholders = new(StringComparer.OrdinalIgnoreCase);

    // Names whose resolution is already in the log: WinUI asks for the same resources again and again.
    private readonly HashSet<string> _reported = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The candidate for the resource <paramref name="name"/>, asked for with the qualifier values
    /// <paramref name="qualifiers"/>, or <see langword="null"/> when it stays unresolved.
    /// </summary>
    public TCandidate? Resolve(string name, IReadOnlyList<KeyValuePair<string, string>> qualifiers)
    {
        // 1. A staged PRI file knows the resource.
        if (FindInStagedPris(name, qualifiers) is { } found)
        {
            if (IsFirstReport(name))
            {
                host.Log(StagedResourceLogLevel.Trace, $"Resource '{name}' [{StagedResourceLookup.DescribeQualifiers(qualifiers)}] was found in the staged PRI '{found.PriFile}' as {host.Describe(found.Candidate)}.");
            }

            return found.Candidate;
        }

        // 2. The staged file at the exact path of the URI, such as an image in a folder named after a qualifier
        // (theme-dark/logo.svg), which no PRI file lists under that name, or the XBF of a library.
        if (contentDirectory is { } root && StagedResourceLookup.FindStagedFile(root, name, host.FileExists) is { } path)
        {
            if (IsFirstReport(name))
            {
                ReportStagedFile(name, path);
            }

            return host.CreateFileCandidate(path);
        }

        // 3. An image that is neither: an empty image, so that the document can still be drawn.
        if (MissingImagePlaceholder.IsImage(name))
        {
            string placeholder = GetPlaceholder(name);
            if (IsFirstReport(name))
            {
                host.Log(StagedResourceLogLevel.Warning, $"The image '{StagedResourceLookup.ToAppUri(name)}' is in none of the {host.PriFiles.Count} staged PRI files and is not a staged file in '{contentDirectory}'; the designer shows it empty.");
            }

            return host.CreateFileCandidate(placeholder);
        }

        // 4. Anything else stays unresolved, as before; the line shows what the framework asked for.
        if (IsFirstReport(name))
        {
            host.Log(StagedResourceLogLevel.Trace, $"Resource '{name}' [{StagedResourceLookup.DescribeQualifiers(qualifiers)}] is in none of the {host.PriFiles.Count} staged PRI files and is not a staged file; it stays unresolved.");
        }

        return null;
    }

    /// <summary>
    /// Writes to the log, once per name, that resolving the resource <paramref name="name"/> failed.
    /// </summary>
    public void ReportFailure(string name, Exception exception)
    {
        if (IsFirstReport($"failed|{name}"))
        {
            host.Log(StagedResourceLogLevel.Warning, $"Resolving the resource '{name}' failed; it stays unresolved.", exception);
        }
    }

    // The first usable candidate of the staged PRI files, in their order.
    private StagedCandidate? FindInStagedPris(string name, IReadOnlyList<KeyValuePair<string, string>> qualifiers)
    {
        lock (_unknownNames)
        {
            if (_unknownNames.Contains(name))
            {
                return null;
            }
        }

        for (int index = 0; index < host.PriFiles.Count; index++)
        {
            // The candidate for the qualifier values of the request (scale, theme, contrast), then the one for the
            // defaults of the PRI file.
            TCandidate? candidate = qualifiers.Count > 0 ? GetUsable(host.LookUp(index, name, qualifiers)) : null;
            candidate ??= GetUsable(host.LookUp(index, name, null));
            if (candidate is not null)
            {
                return new StagedCandidate(host.PriFiles[index], candidate);
            }
        }

        lock (_unknownNames)
        {
            _unknownNames.Add(name);
        }

        return null;
    }

    // The candidate when it can be used: data, such as an XBF that the PRI file embeds, or a file that exists. The same
    // PRI file is staged in several folders (the root, AppX and one folder per package below ProjectResources), and most
    // of its copies name files that were never staged next to them.
    private TCandidate? GetUsable(TCandidate? candidate)
    {
        // A PRI file that does not know the name.
        if (candidate is null)
        {
            return null;
        }

        // Data.
        if (host.GetFilePath(candidate) is not { } path)
        {
            return candidate;
        }

        // A file that is there.
        return host.FileExists(path) ? candidate : null;
    }

    // The staged file of a resource that no staged PRI file answers.
    private void ReportStagedFile(string name, string path)
    {
        // XAML, such as the styles of a library: the output of its last build, not the text in the editor. An edit that is
        // not built yet does not show in the designer, so the line is Information and shows without verbose logging.
        if (StagedResourceLookup.IsMarkup(name))
        {
            host.Log(
                StagedResourceLogLevel.Information,
                FormattableString.Invariant($"Resource '{name}' comes from the build output '{path}' of {host.GetLastWriteTime(path):yyyy-MM-dd HH:mm:ss} (local time); changes made after that build are not shown. It is in none of the {host.PriFiles.Count} staged PRI files.")
            );
            return;
        }

        // Any other file, such as an image in a folder named after a qualifier.
        host.Log(StagedResourceLogLevel.Trace, $"Resource '{name}' is in none of the {host.PriFiles.Count} staged PRI files; the staged file '{path}' is used.");
    }

    // The placeholder file for a missing image, written once.
    private string GetPlaceholder(string name)
    {
        string fileName = MissingImagePlaceholder.GetFileName(name);
        lock (_placeholders)
        {
            if (!_placeholders.TryGetValue(fileName, out string? path))
            {
                path = host.WritePlaceholder(name);
                _placeholders.Add(fileName, path);
            }

            return path;
        }
    }

    // Whether the line is written to the log for the first time.
    private bool IsFirstReport(string key)
    {
        lock (_reported)
        {
            return _reported.Add(key);
        }
    }

    // A candidate of a staged PRI file.
    private sealed record StagedCandidate(string PriFile, TCandidate Candidate);
}
