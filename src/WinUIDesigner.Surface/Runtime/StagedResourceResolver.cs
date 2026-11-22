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
/// PRI file, the staged file at the path of the URI, an empty image for an image, or nothing. What was done is written to
/// the log once per name (what answered a request once per name and qualifier values), because WinUI asks for the same
/// resource again and again. The members can be called from any thread.
/// </summary>
/// <typeparam name="TCandidate">What a resource resolves to; <c>ResourceCandidate</c> in the surface.</typeparam>
internal sealed class StagedResourceResolver<TCandidate>(string? contentDirectory, IStagedResourceHost<TCandidate> host)
    where TCandidate : class
{
    // What the staged PRI files said about the requests that none of them answers, neither for their qualifier values nor
    // for the defaults, by the name and the qualifier values. The PRI files are loaded once, when the surface starts.
    private readonly Dictionary<string, PriAnswer> _misses = new(StringComparer.OrdinalIgnoreCase);

    // The placeholder files by file name, each written once.
    private readonly Dictionary<string, string> _placeholders = new(StringComparer.OrdinalIgnoreCase);

    // The lines that are already in the log.
    private readonly HashSet<string> _reported = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The candidate for the resource <paramref name="name"/>, asked for with the qualifier values
    /// <paramref name="qualifiers"/>, or <see langword="null"/> when it stays unresolved.
    /// </summary>
    public TCandidate? Resolve(string name, IReadOnlyList<KeyValuePair<string, string>> qualifiers)
    {
        string request = StagedResourceLookup.DescribeQualifiers(qualifiers);
        PriAnswer answer = AskStagedPris(name, qualifiers, request);

        // 1. A staged PRI file answers it.
        if (answer.Candidate is { } candidate)
        {
            ReportOnce(
                $"found|{name}|{request}",
                StagedResourceLogLevel.Trace,
                () => $"Resource '{name}' [{request}] was found in the staged PRI '{answer.PriFile}' as {host.Describe(candidate)}."
            );

            return candidate;
        }

        // 2. The staged file at the exact path of the URI, such as an image in a folder named after a qualifier
        // (theme-dark/logo.svg), which no PRI file lists under that name, or the XBF of a library.
        if (contentDirectory is { } root && StagedResourceLookup.FindStagedFile(root, name, host.FileExists) is { } path)
        {
            ReportStagedFile(name, path, answer);
            return host.CreateFileCandidate(path);
        }

        // 3. An image that is neither: an empty image, so that the document can still be drawn.
        if (MissingImagePlaceholder.IsImage(name))
        {
            string placeholder = GetPlaceholder(name);
            ReportOnce(
                $"placeholder|{name}",
                StagedResourceLogLevel.Warning,
                () => $"The image '{StagedResourceLookup.ToAppUri(name)}' {DescribeMiss(answer)}, and it is not a staged file in '{contentDirectory}'; the designer shows it empty."
            );

            return host.CreateFileCandidate(placeholder);
        }

        // 4. Anything else stays unresolved, as before; the line shows what the framework asked for.
        ReportOnce(
            $"unresolved|{name}",
            StagedResourceLogLevel.Trace,
            () => $"Resource '{name}' [{request}] {DescribeMiss(answer)}, and it is not a staged file; it stays unresolved."
        );

        return null;
    }

    /// <summary>
    /// Writes to the log, once per name, that resolving the resource <paramref name="name"/> failed.
    /// </summary>
    public void ReportFailure(string name, Exception exception)
    {
        ReportOnce(
            $"failed|{name}",
            StagedResourceLogLevel.Warning,
            () => $"Resolving the resource '{name}' failed; it stays unresolved.",
            exception
        );
    }

    // The first usable candidate of the staged PRI files, in their order, or, for a request that none of them answers,
    // what they said about it. A PRI file that fails is left out, so that the other PRI files, the staged files and the
    // placeholder still answer.
    private PriAnswer AskStagedPris(string name, IReadOnlyList<KeyValuePair<string, string>> qualifiers, string request)
    {
        // An empty name is no resource. MRT Core rejects it, which every PRI file would report as a failure of its own.
        if (string.IsNullOrEmpty(name))
        {
            return new PriAnswer(null, null, 0, 0, null);
        }

        // A miss of an earlier request. It belongs to the qualifier values of that request, because a resource can have
        // candidates for one theme or one scale only: the same name is asked again for others.
        string key = $"{name}|{request}";
        lock (_misses)
        {
            if (_misses.TryGetValue(key, out PriAnswer? known))
            {
                return known;
            }
        }

        int knownBy = 0;
        int failed = 0;
        string? firstUnusableFile = null;
        for (int index = 0; index < host.PriFiles.Count; index++)
        {
            // The candidate for the qualifier values of the request (scale, theme, contrast), then the one for the
            // defaults of the PRI file.
            PriOutcome outcome = default;
            TCandidate? candidate = qualifiers.Count > 0 ? Ask(index, name, qualifiers, ref outcome) : null;
            candidate ??= Ask(index, name, null, ref outcome);

            // This PRI file answers.
            if (candidate is not null)
            {
                return new PriAnswer(candidate, host.PriFiles[index], 0, 0, null);
            }

            // It does not: what it said is kept for the log.
            knownBy += outcome.UnusableFile is null ? 0 : 1;
            failed += outcome.Failed ? 1 : 0;
            firstUnusableFile ??= outcome.UnusableFile;
        }

        // None of them answers: remembered, so that the PRI files are not asked again for these qualifier values.
        var miss = new PriAnswer(null, null, knownBy, failed, firstUnusableFile);
        lock (_misses)
        {
            _misses.TryAdd(key, miss);
        }

        return miss;
    }

    // One lookup in the PRI file at the index: its candidate when it can be used. A PRI file that fails is reported once.
    private TCandidate? Ask(int index, string name, IReadOnlyList<KeyValuePair<string, string>>? qualifiers, ref PriOutcome outcome)
    {
        try
        {
            // A PRI file that does not know the name.
            if (host.LookUp(index, name, qualifiers) is not { } candidate)
            {
                return null;
            }

            // Data, such as an XBF that the PRI file embeds, or a file that can be used.
            if (host.GetFilePath(candidate) is not { } path || IsUsableFile(path))
            {
                return candidate;
            }

            // A file that is not staged, or one of the stale AppX layout.
            outcome.UnusableFile ??= path;
            return null;
        }
        catch (Exception ex)
        {
            // The PRI file is left out of this lookup, and reported once.
            outcome.Failed = true;
            string priFile = host.PriFiles[index];
            ReportOnce(
                $"failed-pri|{priFile}",
                StagedResourceLogLevel.Warning,
                () => $"Looking up the resource '{name}' in the staged PRI '{priFile}' failed; the other PRI files and the staged files are still asked, and further failures of this PRI file are not logged.",
                ex
            );

            return null;
        }
    }

    // Whether a file that a staged PRI file names can be used: it exists outside the AppX folder. The same PRI file is
    // staged in several folders (the root, AppX and one folder per package below ProjectResources), and each copy names
    // files next to itself: nothing but PRI files is staged below ProjectResources, and the files in AppX are older than
    // the ones at the root.
    private bool IsUsableFile(string path)
    {
        return (contentDirectory is null || !StagedResourceLookup.IsInAppXLayout(contentDirectory, path)) && host.FileExists(path);
    }

    // The staged file of a resource that no staged PRI file answers.
    private void ReportStagedFile(string name, string path, PriAnswer miss)
    {
        // XAML, such as the styles of a library: the output of its last build, not the text in the editor. An edit that is
        // not built yet does not show in the designer, so the line is Information and shows without verbose logging.
        if (StagedResourceLookup.IsMarkup(name))
        {
            ReportOnce(
                $"build-output|{name}",
                StagedResourceLogLevel.Information,
                () => FormattableString.Invariant($"Resource '{name}' comes from the build output '{path}' of {host.GetLastWriteTime(path):yyyy-MM-dd HH:mm:ss} (local time); changes made after that build are not shown. It {DescribeMiss(miss)}.")
            );

            return;
        }

        // Any other file, such as an image in a folder named after a qualifier.
        ReportOnce(
            $"staged-file|{name}",
            StagedResourceLogLevel.Trace,
            () => $"Resource '{name}' {DescribeMiss(miss)}; the staged file '{path}' is used."
        );
    }

    // For the log: "is in none of the 419 staged PRI files", or "is in 2 of the 419 staged PRI files, but none of them
    // names a staged file outside the AppX folder (the first one names '...')", and how many of them failed.
    private string DescribeMiss(PriAnswer miss)
    {
        int count = host.PriFiles.Count;
        string known = miss.KnownBy == 0
            ? $"is in none of the {count} staged PRI files"
            : $"is in {miss.KnownBy} of the {count} staged PRI files, but none of them names a staged file outside the AppX folder (the first one names '{miss.FirstUnusableFile}')";

        return miss.Failed == 0 ? known : $"{known} ({miss.Failed} of them failed to look it up)";
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

    // Writes a line to the log, the first time for the key. The line is a diagnostic and is built here, so that whatever
    // fails while it is built or written does not change the answer to the request, which the framework may not ask again.
    private void ReportOnce(string key, StagedResourceLogLevel level, Func<string> message, Exception? exception = null)
    {
        lock (_reported)
        {
            if (!_reported.Add(key))
            {
                return;
            }
        }

        try
        {
            host.Log(level, message(), exception);
        }
        catch (Exception ex)
        {
            ReportLostLine(key, ex);
        }
    }

    // The line is lost, so the log is told why, when it takes anything.
    private void ReportLostLine(string key, Exception exception)
    {
        try
        {
            host.Log(StagedResourceLogLevel.Warning, $"A line of the log ({key}) could not be built or written; the answer to the request does not depend on it.", exception);
        }
        catch (Exception)
        {
            // The log itself fails: nothing is left to tell.
        }
    }

    // What the staged PRI files answered: a usable candidate and the PRI file that has it, or, when none has one, how many
    // of them know the name (with files that are not staged or only in the AppX folder), how many failed, and the first
    // file that one of them named.
    private sealed record PriAnswer(TCandidate? Candidate, string? PriFile, int KnownBy, int Failed, string? FirstUnusableFile);

    // What one PRI file said about a request that it did not answer.
    private struct PriOutcome
    {
        public bool Failed;

        public string? UnusableFile;
    }
}
