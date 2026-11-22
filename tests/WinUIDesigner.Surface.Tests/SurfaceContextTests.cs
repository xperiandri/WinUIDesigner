// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinUIDesigner.Surface.Runtime;

[TestClass]
public sealed class SurfaceContextTests
{
    private const string _Directory = @"C:\Cache\1-1643526825x64DA";
    private const string _AppXaml = "<Application xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" />";

    [TestMethod]
    public void TheSameStagedFilesAndAppXamlNameTheSameContext()
    {
        string context = SurfaceContext.Identify(_Directory, "12 files", _AppXaml);

        Assert.AreEqual(context, SurfaceContext.Identify(_Directory.ToLowerInvariant(), "12 files", _AppXaml));
        StringAssert.Matches(context, new System.Text.RegularExpressions.Regex("^[0-9A-F]{16}$"));
    }

    [TestMethod]
    public void AChangeOfAppXamlNamesAnotherContext()
    {
        Assert.AreNotEqual(
            SurfaceContext.Identify(_Directory, "12 files", _AppXaml),
            SurfaceContext.Identify(_Directory, "12 files", _AppXaml.Replace(" />", " RequestedTheme=\"Dark\" />"))
        );
    }

    [TestMethod]
    public void AChangeOfTheStagedFilesNamesAnotherContext()
    {
        Assert.AreNotEqual(SurfaceContext.Identify(_Directory, "12 files", _AppXaml), SurfaceContext.Identify(_Directory, "13 files", _AppXaml));
    }

    [TestMethod]
    public void TheStagedFilesChangeWhenTheProjectIsStagedAgain()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"WinUIDesigner.SurfaceContextTests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string assembly = Path.Combine(directory, "App.dll");
            File.WriteAllText(assembly, "built");
            File.SetLastWriteTimeUtc(assembly, new DateTime(2026, 11, 22, 5, 22, 46, DateTimeKind.Utc));
            string staged = SurfaceContext.DescribeStagedFiles(directory);

            Assert.AreEqual(staged, SurfaceContext.DescribeStagedFiles(directory));

            // A build writes the output again.
            File.SetLastWriteTimeUtc(assembly, new DateTime(2026, 11, 22, 5, 40, 0, DateTimeKind.Utc));
            string rebuilt = SurfaceContext.DescribeStagedFiles(directory);

            Assert.AreNotEqual(staged, rebuilt);

            // Visual Studio stages a file that was missing.
            File.WriteAllText(Path.Combine(directory, "resources.pri"), "resources");
            File.SetLastWriteTimeUtc(Path.Combine(directory, "resources.pri"), new DateTime(2026, 11, 22, 5, 0, 0, DateTimeKind.Utc));

            Assert.AreNotEqual(rebuilt, SurfaceContext.DescribeStagedFiles(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void ADirectoryThatCannotBeReadHasNoStagedFiles()
    {
        string missing = Path.Combine(Path.GetTempPath(), $"WinUIDesigner.SurfaceContextTests.{Guid.NewGuid():N}");

        Assert.AreEqual(string.Empty, SurfaceContext.DescribeStagedFiles(missing));
        Assert.AreEqual(string.Empty, SurfaceContext.DescribeStagedFiles(string.Empty));
    }
}
