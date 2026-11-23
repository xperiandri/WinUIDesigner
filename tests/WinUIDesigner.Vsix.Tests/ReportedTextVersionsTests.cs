// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinUIDesigner.Markup;

/// <summary>
/// Covers the record that keeps the designer from reporting the same text of a document on every request.
/// </summary>
[TestClass]
public sealed class ReportedTextVersionsTests
{
    [TestMethod]
    public void TextIsReportedTheFirstTimeItIsSeenAndNotAgain()
    {
        var reported = new ReportedTextVersions();

        Assert.IsTrue(reported.TryRecord(documentId: 7, textVersion: 3, textLength: 120));
        Assert.IsFalse(reported.TryRecord(documentId: 7, textVersion: 3, textLength: 120));
        Assert.IsFalse(reported.TryRecord(documentId: 7, textVersion: 3, textLength: 120));
    }

    [TestMethod]
    public void TextThatChangedIsReportedAgain()
    {
        var reported = new ReportedTextVersions();
        reported.TryRecord(documentId: 7, textVersion: 3, textLength: 120);

        Assert.IsTrue(reported.TryRecord(documentId: 7, textVersion: 4, textLength: 120));
        Assert.IsFalse(reported.TryRecord(documentId: 7, textVersion: 4, textLength: 120));
    }

    [TestMethod]
    public void LengthTellsTextsApartWhereTheBufferDoesNotCountVersions()
    {
        // Some buffers report version 0 for every text.
        var reported = new ReportedTextVersions();
        reported.TryRecord(documentId: 7, textVersion: 0, textLength: 120);

        Assert.IsTrue(reported.TryRecord(documentId: 7, textVersion: 0, textLength: 131));
        Assert.IsFalse(reported.TryRecord(documentId: 7, textVersion: 0, textLength: 131));
    }

    [TestMethod]
    public void EveryDocumentHasItsOwnRecord()
    {
        var reported = new ReportedTextVersions();
        reported.TryRecord(documentId: 7, textVersion: 3, textLength: 120);

        Assert.IsTrue(reported.TryRecord(documentId: 8, textVersion: 3, textLength: 120));
        Assert.IsFalse(reported.TryRecord(documentId: 7, textVersion: 3, textLength: 120));
        Assert.IsFalse(reported.TryRecord(documentId: 8, textVersion: 3, textLength: 120));
    }

    [TestMethod]
    public void OnlyTheLatestTextOfADocumentIsRemembered()
    {
        // After an undo the text is the one of an earlier version again: it is reported again, because the
        // record keeps one text for each document, not a list that grows with every edit.
        var reported = new ReportedTextVersions();
        reported.TryRecord(documentId: 7, textVersion: 3, textLength: 120);
        reported.TryRecord(documentId: 7, textVersion: 4, textLength: 121);

        Assert.IsTrue(reported.TryRecord(documentId: 7, textVersion: 3, textLength: 120));
    }
}
