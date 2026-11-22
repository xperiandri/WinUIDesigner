// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinUIDesigner.Surface.Runtime;

[TestClass]
public sealed class StagedResourceLookupTests
{
    // A content directory as Visual Studio stages it. Nothing is read from it or written to it: the tests decide which
    // files exist.
    private static readonly string _Root = Path.Combine(Path.GetTempPath(), "WinUIDesigner.Surface.Tests", "1-1643526825x64DA");

    private static string Staged(string relativePath) => Path.Combine([_Root, .. relativePath.Split('/')]);

    [TestMethod]
    [DataRow("Files/Assets/Images/svg/theme-light/ecierge_waves_logo.svg", "Assets/Images/svg/theme-light/ecierge_waves_logo.svg")]
    [DataRow("Files/Ecierge.Uno.Controls/Styles.xbf", "Ecierge.Uno.Controls/Styles.xbf")]
    [DataRow("Files/Ecierge.Uno.Controls/themes/generic.xbf", "Ecierge.Uno.Controls/themes/generic.xbf")]
    [DataRow("Files/Assets/Images/back.png", "Assets/Images/back.png")]
    [DataRow("files/Assets/logo.png", "Assets/logo.png")]
    [DataRow("Files/Assets/My%20Logo.png", "Assets/My Logo.png")]
    public void FileResourceNamesTheStagedFileAtThePathOfItsUri(string name, string relativePath)
    {
        // An image in a folder named after a qualifier (which no PRI file lists under that name), the XBF of a library
        // and the default styles of its controls, a plain image, a prefix in other case and an escaped name.
        Assert.AreEqual(Staged(relativePath), StagedResourceLookup.GetStagedPath(_Root, name));
    }

    [TestMethod]
    [DataRow("Microsoft.UI.Xaml/Resources/x")]
    [DataRow("Resources/AppDisplayName")]
    [DataRow("Assets/logo.png")]
    [DataRow("ms-appx:///Assets/logo.png")]
    [DataRow("")]
    public void NamesOfOtherResourcesNameNoFile(string name)
    {
        Assert.IsNull(StagedResourceLookup.GetStagedPath(_Root, name));
    }

    [TestMethod]
    [DataRow("Files/../outside.png")]
    [DataRow("Files/Assets/../../outside.png")]
    [DataRow("Files/Assets/%2e%2e/%2E%2E/outside.png")]
    [DataRow("Files/Assets%2F..%2F..%2Foutside.png")]
    [DataRow("Files/Assets%5C..%5C..%5Coutside.png")]
    [DataRow("Files/./logo.png")]
    [DataRow("Files/%2e/logo.png")]
    [DataRow("Files//logo.png")]
    [DataRow("Files/Assets/")]
    [DataRow("Files/")]
    [DataRow("Files/C:/Windows/win.ini")]
    [DataRow("Files/C:%5CWindows%5Cwin.ini")]
    [DataRow("Files/%5C%5Cserver%5Cshare/logo.png")]
    [DataRow("Files/logo.png%3A%3A%24DATA")]
    [DataRow("Files/logo%3F.png")]
    [DataRow("Files/logo*.png")]
    [DataRow("Files/logo%00.png")]
    public void NamesThatLeaveTheRootOrNameNoFileAreRefused(string name)
    {
        // Parent and current folders, also escaped; escaped separators; empty segments; rooted segments; a stream;
        // characters that no file name has.
        Assert.IsNull(StagedResourceLookup.GetStagedPath(_Root, name));
    }

    [TestMethod]
    [DataRow("Files/AppX/Assets/logo.png")]
    [DataRow("Files/appx/Ecierge.Console.UI/Styles.xbf")]
    [DataRow("Files/App%58/Assets/logo.png")]
    public void TheStaleAppXLayoutIsNeverUsed(string name)
    {
        Assert.IsNull(StagedResourceLookup.GetStagedPath(_Root, name));
    }

    [TestMethod]
    public void ExistingStagedFileIsFound()
    {
        var asked = new List<string>();

        string? found = StagedResourceLookup.FindStagedFile(
            _Root,
            "Files/Ecierge.Uno.Controls/Styles.xbf",
            path =>
            {
                asked.Add(path);
                return true;
            }
        );

        Assert.AreEqual(Staged("Ecierge.Uno.Controls/Styles.xbf"), found);
        Assert.AreEqual(Staged("Ecierge.Uno.Controls/Styles.xbf"), Assert.ContainsSingle(asked));
    }

    [TestMethod]
    public void MissingStagedFileIsNotFound()
    {
        Assert.IsNull(StagedResourceLookup.FindStagedFile(_Root, "Files/Assets/Images/svg/theme-light/logo.svg", _ => false));
    }

    [TestMethod]
    public void FileThatIsOnlyInTheAppXLayoutIsNotFound()
    {
        // AppX\Ecierge.Console.UI\Styles.xbf was built weeks before the files at the root.
        string stale = Staged("AppX/Ecierge.Console.UI/Styles.xbf");

        Assert.IsNull(StagedResourceLookup.FindStagedFile(_Root, "Files/Ecierge.Console.UI/Styles.xbf", path => path == stale));
    }

    [TestMethod]
    public void PriFilesAtTheRootComeFirstAndTheApplicationsFirstAmongThem()
    {
        string[] files =
        [
            Staged("ProjectResources/Ecierge.Console/resources.pri"),
            Staged("AppX/Microsoft.UI.Xaml.Controls.pri"),
            Staged("Microsoft.UI.Xaml.Controls.pri"),
            Staged("AppX/resources.pri"),
            Staged("resources.pri"),
            Staged("Ecierge.Console.UI.pri"),
        ];

        CollectionAssert.AreEqual(
            new[]
            {
                Staged("resources.pri"),
                Staged("Ecierge.Console.UI.pri"),
                Staged("Microsoft.UI.Xaml.Controls.pri"),
                Staged("AppX/resources.pri"),
                Staged("AppX/Microsoft.UI.Xaml.Controls.pri"),
                Staged("ProjectResources/Ecierge.Console/resources.pri"),
            },
            StagedResourceLookup.OrderPriFiles(_Root, files)
        );
    }

    [TestMethod]
    public void FileCandidateWhoseFileDoesNotExistFallsThroughToTheNextOne()
    {
        // The copy of the application's PRI file below ProjectResources names files next to itself, where nothing is
        // staged; the copy at the root names the staged file.
        var missing = new Candidate(Staged("ProjectResources/Ecierge.Console/Assets/Images/back.scale-150.png"));
        var staged = new Candidate(Staged("Assets/Images/back.scale-150.png"));

        Candidate? found = StagedResourceLookup.FirstUsable<Candidate>(
            [missing, null, staged],
            candidate => candidate.FilePath,
            path => path == staged.FilePath
        );

        Assert.AreSame(staged, found);
    }

    [TestMethod]
    public void CandidateThatNamesNoFileIsUsedAsItIs()
    {
        // An XBF that the PRI file embeds.
        var embedded = new Candidate(null);

        Candidate? found = StagedResourceLookup.FirstUsable<Candidate>(
            [null, embedded],
            candidate => candidate.FilePath,
            _ => throw new AssertFailedException("A candidate that names no file has no file to look for.")
        );

        Assert.AreSame(embedded, found);
    }

    [TestMethod]
    public void NoUsableCandidateGivesNone()
    {
        Candidate? found = StagedResourceLookup.FirstUsable<Candidate>(
            [null, new Candidate(Staged("AppX/Assets/missing.png")), null],
            candidate => candidate.FilePath,
            _ => false
        );

        Assert.IsNull(found);
    }

    [TestMethod]
    public void CandidatesAfterTheUsableOneAreNotAsked()
    {
        // Each candidate is a lookup in a PRI file.
        var asked = new List<int>();
        IEnumerable<Candidate?> Lookups()
        {
            for (int index = 0; index < 4; index++)
            {
                asked.Add(index);
                yield return index == 1 ? new Candidate(null) : null;
            }
        }

        Assert.IsNotNull(StagedResourceLookup.FirstUsable(Lookups(), candidate => candidate.FilePath, _ => true));
        CollectionAssert.AreEqual(new[] { 0, 1 }, asked);
    }

    [TestMethod]
    [DataRow("Files/Assets/Images/svg/theme-light/ecierge_waves_logo.svg", "ms-appx:///Assets/Images/svg/theme-light/ecierge_waves_logo.svg")]
    [DataRow("Resources/AppDisplayName", "Resources/AppDisplayName")]
    public void FileResourceIsShownByItsUri(string name, string expected)
    {
        Assert.AreEqual(expected, StagedResourceLookup.ToAppUri(name));
    }

    [TestMethod]
    public void QualifiersThatAreSetAreDescribedByName()
    {
        KeyValuePair<string, string>[] qualifiers = [new("Theme", "Dark"), new("Contrast", ""), new("Scale", "150")];

        Assert.AreEqual("Scale=150, Theme=Dark", StagedResourceLookup.DescribeQualifiers(qualifiers));
        Assert.AreEqual(string.Empty, StagedResourceLookup.DescribeQualifiers([]));
    }

    private sealed record Candidate(string? FilePath);
}
