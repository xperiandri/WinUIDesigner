// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinUIDesigner.Platform;

/// <summary>
/// The sources that name a file of a project by its assembly name, as the XAML of the Ecierge solution writes them
/// (<c>ms-appx:///Ecierge.Uno.Controls/Styles.xaml</c>), and the forms that Visual Studio resolves without help.
/// </summary>
[TestClass]
public sealed class AppxLibraryUriTests
{
    [TestMethod]
    [DataRow("ms-appx:///Ecierge.Uno.Controls/Styles.xaml", "Ecierge.Uno.Controls", "\\Styles.xaml")]
    [DataRow("ms-appx:///Ecierge.Uno.Controls/Themes/Generic.xaml", "Ecierge.Uno.Controls", "\\Themes\\Generic.xaml")]
    [DataRow("MS-APPX:///Ecierge.Uno.Controls/Styles.xaml", "Ecierge.Uno.Controls", "\\Styles.xaml")]
    [DataRow("  ms-appx:///Ecierge.Uno.Controls/Styles.xaml  ", "Ecierge.Uno.Controls", "\\Styles.xaml")]
    [DataRow("ms-appx:///Ecierge.Uno.Controls/My%20Dictionary.xaml", "Ecierge.Uno.Controls", "\\My Dictionary.xaml")]
    [DataRow("/Ecierge.Uno.Controls/Styles.xaml", "Ecierge.Uno.Controls", "\\Styles.xaml")]
    [DataRow("/Ecierge.Uno.Controls/Styles/Buttons.xaml", "Ecierge.Uno.Controls", "\\Styles\\Buttons.xaml")]
    public void SourceThatNamesAProjectByItsAssemblyIsSplit(string value, string expectedAssembly, string expectedPath)
    {
        Assert.IsTrue(AppxLibraryUri.TrySplit(value, out string assemblyName, out string projectRelativePath));
        Assert.AreEqual(expectedAssembly, assemblyName);
        Assert.AreEqual(expectedPath, projectRelativePath);
    }

    [TestMethod]
    [DataRow("ms-appx://Some.Package/Ecierge.Uno.Controls/Styles.xaml")]
    [DataRow("ms-appx:///Ecierge.Uno.Controls/~vs]~12~Styles.xaml")]
    [DataRow("ms-appx:///Styles.xaml")]
    [DataRow("ms-appx:///Ecierge.Uno.Controls/")]
    [DataRow("ms-appx:///")]
    [DataRow("/Styles.xaml")]
    [DataRow("Styles.xaml")]
    [DataRow("Themes/Styles.xaml")]
    [DataRow("../Ecierge.Uno.Controls/Styles.xaml")]
    [DataRow("//server/share/Styles.xaml")]
    [DataRow("https://example.com/Ecierge.Uno.Controls/Styles.xaml")]
    [DataRow("ms-resource:///Files/Ecierge.Uno.Controls/Styles.xaml")]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow(null)]
    public void OtherSourcesAreLeftToVisualStudio(string? value)
    {
        // A URI with a host names a package, a prepared copy is found by its id, a file in the root of the package
        // belongs to the application, and a path without a leading slash is relative to its document.
        Assert.IsFalse(AppxLibraryUri.TrySplit(value, out string assemblyName, out string projectRelativePath));
        Assert.AreEqual(string.Empty, assemblyName);
        Assert.AreEqual(string.Empty, projectRelativePath);
    }
}
