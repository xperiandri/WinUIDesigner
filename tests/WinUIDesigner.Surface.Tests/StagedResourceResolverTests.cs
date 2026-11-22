// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinUIDesigner.Surface.Runtime;

[TestClass]
public sealed class StagedResourceResolverTests
{
    // A content directory as Visual Studio stages it. Nothing is read from it or written to it: the host decides which
    // files exist.
    private static readonly string _Root = Path.Combine(Path.GetTempPath(), "WinUIDesigner.Surface.Tests", "1-1643526825x64DA");

    private static readonly KeyValuePair<string, string>[] _NoQualifiers = [];

    private static string Staged(string relativePath) => Path.Combine([_Root, .. relativePath.Split('/')]);

    [TestMethod]
    public void PriFileThatKnowsTheNameAnswersIt()
    {
        // An XBF that the PRI file embeds.
        var embedded = new Candidate(null);
        var host = new FakeHost("resources.pri", "other.pri");
        host.Knows(0, "Files/App.xbf", embedded);
        var resolver = new StagedResourceResolver<Candidate>(_Root, host);

        Assert.AreSame(embedded, resolver.Resolve("Files/App.xbf", _NoQualifiers));
        CollectionAssert.AreEqual(new[] { "0 Files/App.xbf defaults" }, host.Lookups);
        Assert.Contains("was found in the staged PRI 'resources.pri'", Assert.ContainsSingle(host.Lines).Message);
    }

    [TestMethod]
    public void PriFileWinsOverAStagedFileOfTheSameName()
    {
        var known = new Candidate(Staged("Assets/Images/back.scale-150.png"));
        var host = new FakeHost("resources.pri");
        host.Knows(0, "Files/Assets/Images/back.png", known);
        host.Files.Add(known.FilePath!);
        host.Files.Add(Staged("Assets/Images/back.png"));
        var resolver = new StagedResourceResolver<Candidate>(_Root, host);

        Assert.AreSame(known, resolver.Resolve("Files/Assets/Images/back.png", _NoQualifiers));
    }

    [TestMethod]
    public void EachPriFileIsAskedForTheQualifierValuesOfTheRequestAndThenForItsDefaults()
    {
        var host = new FakeHost("resources.pri", "other.pri");
        var resolver = new StagedResourceResolver<Candidate>(_Root, host);

        Assert.IsNull(resolver.Resolve("Files/Strings/Resources.resw", [new("Theme", "dark"), new("Scale", "150")]));
        CollectionAssert.AreEqual(
            new[]
            {
                "0 Files/Strings/Resources.resw Scale=150, Theme=dark",
                "0 Files/Strings/Resources.resw defaults",
                "1 Files/Strings/Resources.resw Scale=150, Theme=dark",
                "1 Files/Strings/Resources.resw defaults",
            },
            host.Lookups
        );
    }

    [TestMethod]
    public void QualifiedCandidateWinsOverTheDefaultOfTheSamePriFile()
    {
        var qualified = new Candidate(Staged("Assets/Images/back.scale-150.png"));
        var neutral = new Candidate(Staged("Assets/Images/back.png"));
        var host = new FakeHost("resources.pri");
        host.Knows(0, "Files/Assets/Images/back.png", qualified, "Scale=150");
        host.Knows(0, "Files/Assets/Images/back.png", neutral);
        host.Files.Add(qualified.FilePath!);
        host.Files.Add(neutral.FilePath!);
        var resolver = new StagedResourceResolver<Candidate>(_Root, host);

        Assert.AreSame(qualified, resolver.Resolve("Files/Assets/Images/back.png", [new("Scale", "150")]));
        Assert.AreSame(neutral, resolver.Resolve("Files/Assets/Images/back.png", _NoQualifiers));
    }

    [TestMethod]
    public void FileCandidateWhoseFileDoesNotExistFallsThroughToTheNextPriFile()
    {
        // The copy of the application's PRI file below ProjectResources names files next to itself, where nothing is
        // staged; the copy at the root names the staged file.
        var missing = new Candidate(Staged("ProjectResources/Ecierge.Console/Assets/Images/back.scale-150.png"));
        var staged = new Candidate(Staged("Assets/Images/back.scale-150.png"));
        var host = new FakeHost("resources.pri", "ProjectResources/Ecierge.Console/resources.pri", "Ecierge.Console.pri");
        host.Knows(1, "Files/Assets/Images/back.png", missing);
        host.Knows(2, "Files/Assets/Images/back.png", staged);
        host.Files.Add(staged.FilePath!);
        var resolver = new StagedResourceResolver<Candidate>(_Root, host);

        Assert.AreSame(staged, resolver.Resolve("Files/Assets/Images/back.png", _NoQualifiers));
    }

    [TestMethod]
    public void CandidateThatNamesNoFileIsUsedWithoutLookingForAFile()
    {
        // An XBF that the PRI file embeds.
        var embedded = new Candidate(null);
        var host = new FakeHost("resources.pri", "other.pri");
        host.Knows(1, "Files/App.xbf", embedded);
        var resolver = new StagedResourceResolver<Candidate>(_Root, host);

        Assert.AreSame(embedded, resolver.Resolve("Files/App.xbf", _NoQualifiers));
        Assert.IsEmpty(host.FileChecks);
    }

    [TestMethod]
    public void PriFilesAfterTheAnsweringOneAreNotAsked()
    {
        var host = new FakeHost("a.pri", "b.pri", "c.pri", "d.pri");
        host.Knows(1, "Files/App.xbf", new Candidate(null));
        var resolver = new StagedResourceResolver<Candidate>(_Root, host);

        Assert.IsNotNull(resolver.Resolve("Files/App.xbf", _NoQualifiers));
        CollectionAssert.AreEqual(new[] { "0 Files/App.xbf defaults", "1 Files/App.xbf defaults" }, host.Lookups);
    }

    [TestMethod]
    public void NameThatNoUsableCandidateAnswersTakesTheStagedFileAtThePathOfItsUri()
    {
        // The only PRI file that knows the name names a file that is not staged.
        var host = new FakeHost("resources.pri");
        host.Knows(0, "Files/Ecierge.Uno.Controls/Styles.xbf", new Candidate(Staged("AppX/Assets/missing.xbf")));
        host.Files.Add(Staged("Ecierge.Uno.Controls/Styles.xbf"));
        var resolver = new StagedResourceResolver<Candidate>(_Root, host);

        Assert.AreEqual(new Candidate(Staged("Ecierge.Uno.Controls/Styles.xbf")), resolver.Resolve("Files/Ecierge.Uno.Controls/Styles.xbf", _NoQualifiers));
        Assert.Contains("the staged file", Assert.ContainsSingle(host.Lines).Message);
    }

    [TestMethod]
    public void StagedFileOfTheAppXFolderIsNeverUsed()
    {
        // AppX\Ecierge.Console.UI\Styles.xbf was built weeks before the files at the root.
        var host = new FakeHost("resources.pri");
        host.Files.Add(Staged("AppX/Ecierge.Console.UI/Styles.xbf"));
        var resolver = new StagedResourceResolver<Candidate>(_Root, host);

        Assert.IsNull(resolver.Resolve("Files/Ecierge.Console.UI/Styles.xbf", _NoQualifiers));
    }

    [TestMethod]
    public void NoStagedFilesAreLookedForWithoutAContentDirectory()
    {
        var host = new FakeHost("resources.pri");
        host.Files.Add(Staged("Ecierge.Uno.Controls/Styles.xbf"));
        var resolver = new StagedResourceResolver<Candidate>(null, host);

        Assert.IsNull(resolver.Resolve("Files/Ecierge.Uno.Controls/Styles.xbf", _NoQualifiers));
        Assert.IsEmpty(host.FileChecks);
    }

    [TestMethod]
    public void ImageThatNothingAnswersGetsAPlaceholderAndOneWarning()
    {
        var host = new FakeHost("resources.pri", "other.pri");
        var resolver = new StagedResourceResolver<Candidate>(_Root, host);

        Candidate? first = resolver.Resolve("Files/Assets/Images/svg/theme-light/ecierge_waves_logo.svg", _NoQualifiers);
        Candidate? second = resolver.Resolve("Files/Assets/Images/svg/theme-light/ecierge_waves_logo.svg", [new("Theme", "dark")]);

        Assert.AreEqual(new Candidate(host.PlaceholderPath("missing-image.svg")), first);
        Assert.AreEqual(first, second);
        Line line = Assert.ContainsSingle(host.Lines);
        Assert.AreEqual(StagedResourceLogLevel.Warning, line.Level);
        Assert.AreEqual(
            $"The image 'ms-appx:///Assets/Images/svg/theme-light/ecierge_waves_logo.svg' is in none of the 2 staged PRI files and is not a staged file in '{_Root}'; the designer shows it empty.",
            line.Message
        );
    }

    [TestMethod]
    public void PlaceholderIsWrittenOncePerFileNameAndEveryImageIsReportedOnce()
    {
        var host = new FakeHost("resources.pri");
        var resolver = new StagedResourceResolver<Candidate>(_Root, host);

        foreach (string name in new[] { "Files/Assets/a.png", "Files/Assets/b.jpg", "Files/Assets/c.svg", "Files/Assets/a.png", "Files/Assets/c.svg" })
        {
            Assert.IsNotNull(resolver.Resolve(name, _NoQualifiers));
        }

        // One PNG and one SVG placeholder, three images reported.
        CollectionAssert.AreEqual(new[] { "Files/Assets/a.png", "Files/Assets/c.svg" }, host.Placeholders);
        Assert.HasCount(3, host.Lines);
        Assert.IsTrue(host.Lines.All(line => line.Level == StagedResourceLogLevel.Warning));
    }

    [TestMethod]
    [DataRow("Files/Ecierge.Uno.Controls/Styles.xbf")]
    [DataRow("Files/Ecierge.Uno.Controls/Styles.xaml")]
    [DataRow("Files/Strings/en-US/Resources.resw")]
    public void OtherNameThatNothingAnswersStaysUnresolvedWithOneTrace(string name)
    {
        var host = new FakeHost("resources.pri");
        var resolver = new StagedResourceResolver<Candidate>(_Root, host);

        Assert.IsNull(resolver.Resolve(name, [new("Theme", "dark")]));
        Assert.IsNull(resolver.Resolve(name, [new("Theme", "dark")]));

        // Neither a placeholder nor a warning: an empty XBF, XAML or RESW file would hide an error.
        Assert.IsEmpty(host.Placeholders);
        Line line = Assert.ContainsSingle(host.Lines);
        Assert.AreEqual(StagedResourceLogLevel.Trace, line.Level);
        Assert.AreEqual($"Resource '{name}' [Theme=dark] is in none of the 1 staged PRI files and is not a staged file; it stays unresolved.", line.Message);
    }

    [TestMethod]
    public void NameThatNoPriFileAnswersIsNotLookedUpAgain()
    {
        var host = new FakeHost("resources.pri", "other.pri");
        var resolver = new StagedResourceResolver<Candidate>(_Root, host);

        Assert.IsNull(resolver.Resolve("Files/Strings/Resources.resw", _NoQualifiers));
        int lookups = host.Lookups.Count;

        Assert.IsNull(resolver.Resolve("Files/Strings/Resources.resw", _NoQualifiers));
        Assert.HasCount(lookups, host.Lookups);
    }

    [TestMethod]
    public void FailureIsReportedOncePerName()
    {
        var host = new FakeHost("resources.pri");
        var resolver = new StagedResourceResolver<Candidate>(_Root, host);
        var exception = new InvalidOperationException("MRT said no.");

        resolver.ReportFailure("Files/Assets/a.png", exception);
        resolver.ReportFailure("Files/Assets/a.png", exception);
        resolver.ReportFailure("Files/Assets/b.png", exception);

        Assert.HasCount(2, host.Lines);
        Assert.IsTrue(host.Lines.All(line => line.Level == StagedResourceLogLevel.Warning && line.Exception == exception));
        Assert.Contains("'Files/Assets/a.png'", host.Lines[0].Message);
        Assert.Contains("'Files/Assets/b.png'", host.Lines[1].Message);
    }

    [TestMethod]
    public void RequestsFromSeveralThreadsWriteAndReportOnce()
    {
        var host = new FakeHost("resources.pri");
        var resolver = new StagedResourceResolver<Candidate>(_Root, host);

        Parallel.For(0, 64, _ => Assert.IsNotNull(resolver.Resolve("Files/Assets/logo.png", _NoQualifiers)));

        Assert.ContainsSingle(host.Placeholders);
        Assert.ContainsSingle(host.Lines);
    }

    // What a lookup in a PRI file gives. The surface has a MRT Core candidate in its place.
    private sealed record Candidate(string? FilePath);

    // A line that the resolver wrote to the log.
    private sealed record Line(StagedResourceLogLevel Level, string Message, Exception? Exception);

    // The surface without MRT Core, the file system and the log: the test says what the PRI files know and which files
    // exist, and reads what the resolver asked and wrote.
    private sealed class FakeHost(params string[] priFiles) : IStagedResourceHost<Candidate>
    {
        private readonly object _gate = new();

        // Candidates by PRI file, name and the qualifier values they are for (null: the defaults of the PRI file).
        private readonly Dictionary<(int PriFile, string Name, string? Qualifiers), Candidate> _known = [];

        public IReadOnlyList<string> PriFiles { get; } = priFiles;

        // The files that exist.
        public HashSet<string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

        // "index name qualifier values", for each lookup in a PRI file.
        public List<string> Lookups { get; } = [];

        public List<string> FileChecks { get; } = [];

        // The names of the images that a placeholder was written for.
        public List<string> Placeholders { get; } = [];

        public List<Line> Lines { get; } = [];

        public void Knows(int priFile, string name, Candidate candidate, string? qualifiers = null)
        {
            _known[(priFile, name, qualifiers)] = candidate;
        }

        public string PlaceholderPath(string fileName) => Path.Combine(_Root, "placeholders", fileName);

        public Candidate? LookUp(int priFile, string name, IReadOnlyList<KeyValuePair<string, string>>? qualifiers)
        {
            string? request = qualifiers is null ? null : StagedResourceLookup.DescribeQualifiers(qualifiers);
            lock (_gate)
            {
                Lookups.Add($"{priFile} {name} {request ?? "defaults"}");
                return _known.GetValueOrDefault((priFile, name, request));
            }
        }

        public string? GetFilePath(Candidate candidate) => candidate.FilePath;

        public Candidate CreateFileCandidate(string path) => new(path);

        public string Describe(Candidate candidate) => candidate.FilePath is { } path ? $"the file '{path}'" : "data";

        public bool FileExists(string path)
        {
            lock (_gate)
            {
                FileChecks.Add(path);
                return Files.Contains(path);
            }
        }

        public string WritePlaceholder(string name)
        {
            lock (_gate)
            {
                Placeholders.Add(name);
                return PlaceholderPath(MissingImagePlaceholder.GetFileName(name));
            }
        }

        public void Log(StagedResourceLogLevel level, string message, Exception? exception)
        {
            lock (_gate)
            {
                Lines.Add(new Line(level, message, exception));
            }
        }
    }
}
