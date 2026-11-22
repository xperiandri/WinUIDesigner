// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.VisualStudio.DesignTools.Utility.Clipboard;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinUIDesigner.Platform;

[TestClass]
public sealed class GuardedClipboardTests
{
    // CLIPBRD_E_CANT_OPEN: what WPF reports when another process keeps the clipboard open.
    private const int _ClipboardCannotBeOpened = unchecked((int)0x800401D0);

    [TestMethod]
    public void ReadableClipboardAnswersForItself()
    {
        var data = new DataObject();
        var clipboard = new FakeClipboard { ContainsDataResult = true, Data = "text", DataObject = data, IsCurrentResult = true };
        var reports = new List<Exception?>();
        var guarded = new GuardedClipboard(clipboard, reports.Add);

        Assert.IsTrue(guarded.ContainsData(DataFormats.Text));
        Assert.AreEqual("text", guarded.GetData(DataFormats.Text));
        Assert.AreSame(data, guarded.GetDataObject());
        Assert.IsTrue(guarded.IsCurrent(data));
        Assert.IsEmpty(reports);
    }

    [TestMethod]
    public void ClipboardThatCannotBeOpenedCountsAsEmpty()
    {
        var clipboard = new FakeClipboard { Failure = CreateCannotOpenFailure() };

        // A guard for each read, so that none of them is answered by the retry delay another one started.
        Assert.IsNull(new GuardedClipboard(clipboard).GetData(DataFormats.Text));
        Assert.IsFalse(new GuardedClipboard(clipboard).IsCurrent(new DataObject()));

        IDataObject data = new GuardedClipboard(clipboard).GetDataObject();
        Assert.IsNotNull(data, "Visual Studio uses the data object without checking it.");
        Assert.IsEmpty(data.GetFormats());
        Assert.AreEqual(3, clipboard.Opens);
    }

    [TestMethod]
    public void AccessDeniedCountsAsEmptyToo()
    {
        var clipboard = new FakeClipboard { Failure = new UnauthorizedAccessException() };
        var guarded = new GuardedClipboard(clipboard);

        Assert.IsEmpty(guarded.GetDataObject().GetFormats());
    }

    [TestMethod]
    public void FormatQuestionThatFailsAnswersNoAndStartsNoOutage()
    {
        // WPF fails it only when Windows cannot register a format name it has not seen before.
        var data = new DataObject();
        var clipboard = new FakeClipboard { ContainsDataResult = true, ContainsDataFailure = new Win32Exception(), DataObject = data };
        var reports = new List<Exception?>();
        var guarded = new GuardedClipboard(clipboard, reports.Add, () => TimeSpan.Zero);

        Assert.IsFalse(guarded.ContainsData("A format name"));

        // That says nothing about opening the clipboard: no outage starts, so the next read asks for the data.
        Assert.IsEmpty(reports);
        Assert.AreSame(data, guarded.GetDataObject());
    }

    [TestMethod]
    public void OtherFailuresAreNotHidden()
    {
        var failure = new InvalidOperationException("not a clipboard failure");
        var clipboard = new FakeClipboard { Failure = failure, ContainsDataFailure = failure };
        var guarded = new GuardedClipboard(clipboard);

        Assert.ThrowsExactly<InvalidOperationException>(() => guarded.ContainsData(DataFormats.Text));
        Assert.ThrowsExactly<InvalidOperationException>(() => guarded.GetDataObject());
    }

    [TestMethod]
    public void WritingReportsItsFailureToTheCaller()
    {
        // Visual Studio shows a message when a copy fails; hiding the failure would lose it.
        var clipboard = new FakeClipboard { Failure = CreateCannotOpenFailure() };
        var guarded = new GuardedClipboard(clipboard);

        Assert.ThrowsExactly<COMException>(() => guarded.SetDataObject("text"));
        Assert.ThrowsExactly<COMException>(() => guarded.SetDataObject("text", true));
    }

    [TestMethod]
    public void WritingReachesTheClipboard()
    {
        var clipboard = new FakeClipboard();
        var guarded = new GuardedClipboard(clipboard);

        guarded.SetDataObject("first");
        guarded.SetDataObject("second", true);

        CollectionAssert.AreEqual(new[] { "first", "second (copy)" }, clipboard.Written);
    }

    [TestMethod]
    public void ReadsThatOpenTheClipboardDoNotAskAgainUntilTheRetryDelayHasPassed()
    {
        TimeSpan now = TimeSpan.Zero;
        var clipboard = new FakeClipboard { Failure = CreateCannotOpenFailure() };
        var guarded = new GuardedClipboard(clipboard, getTime: () => now);

        guarded.GetDataObject();
        Assert.AreEqual(1, clipboard.Opens);

        // Each of these would wait for about a second in the retries of WPF, and while text is edited in place Visual
        // Studio reads the data object on every refresh of its commands. A paste in this time gets the same answer.
        now = GuardedClipboard.RetryDelay - TimeSpan.FromMilliseconds(1);
        Assert.IsEmpty(guarded.GetDataObject().GetFormats());
        Assert.IsNull(guarded.GetData(DataFormats.Text));
        Assert.IsFalse(guarded.IsCurrent(new DataObject()));
        Assert.AreEqual(1, clipboard.Opens);

        var data = new DataObject();
        now = GuardedClipboard.RetryDelay;
        clipboard.Failure = null;
        clipboard.DataObject = data;
        Assert.AreSame(data, guarded.GetDataObject());
        Assert.AreEqual(2, clipboard.Opens);
    }

    [TestMethod]
    public void EveryFailureStartsTheRetryDelayAgain()
    {
        TimeSpan now = TimeSpan.Zero;
        var clipboard = new FakeClipboard { Failure = CreateCannotOpenFailure() };
        var guarded = new GuardedClipboard(clipboard, getTime: () => now);

        guarded.GetDataObject();

        now = GuardedClipboard.RetryDelay;
        guarded.GetDataObject();
        Assert.AreEqual(2, clipboard.Opens);

        now = GuardedClipboard.RetryDelay + GuardedClipboard.RetryDelay - TimeSpan.FromMilliseconds(1);
        guarded.GetDataObject();
        Assert.AreEqual(2, clipboard.Opens);
    }

    [TestMethod]
    public void FormatQuestionIsNotDelayed()
    {
        var clipboard = new FakeClipboard { Failure = CreateCannotOpenFailure(), ContainsDataResult = true };
        var guarded = new GuardedClipboard(clipboard, getTime: () => TimeSpan.Zero);
        guarded.GetDataObject();

        // WPF answers it without opening the clipboard, so it neither waits nor fails, and the Paste command
        // stays enabled while the designer's format is on the clipboard.
        Assert.IsTrue(guarded.ContainsData(DataFormats.Text));
        Assert.AreEqual(1, clipboard.Questions);
    }

    [TestMethod]
    public void FormatQuestionDoesNotEndAnOutage()
    {
        TimeSpan now = TimeSpan.Zero;
        var clipboard = new FakeClipboard { Failure = CreateCannotOpenFailure(), ContainsDataResult = true };
        var reports = new List<Exception?>();
        var guarded = new GuardedClipboard(clipboard, reports.Add, () => now);
        guarded.GetDataObject();

        // Windows tells which formats the clipboard holds even while another process keeps it open.
        now = GuardedClipboard.RetryDelay;
        guarded.ContainsData(DataFormats.Text);
        guarded.GetDataObject();

        Assert.HasCount(1, reports);
    }

    [TestMethod]
    public void EachOutageIsReportedOnceAndSoIsItsEnd()
    {
        TimeSpan now = TimeSpan.Zero;
        COMException failure = CreateCannotOpenFailure();
        var clipboard = new FakeClipboard { Failure = failure };
        var reports = new List<Exception?>();
        var guarded = new GuardedClipboard(clipboard, reports.Add, () => now);

        guarded.GetDataObject();
        now = GuardedClipboard.RetryDelay;
        guarded.GetData(DataFormats.Text);
        Assert.HasCount(1, reports);
        Assert.AreSame(failure, reports[0]);

        clipboard.Failure = null;
        now += GuardedClipboard.RetryDelay;
        guarded.GetDataObject();
        guarded.GetDataObject();
        Assert.HasCount(2, reports);
        Assert.IsNull(reports[1]);

        clipboard.Failure = failure;
        guarded.GetDataObject();
        Assert.HasCount(3, reports);
        Assert.AreSame(failure, reports[2]);
    }

    [TestMethod]
    [DataRow(false, DisplayName = "SetDataObject(data)")]
    [DataRow(true, DisplayName = "SetDataObject(data, copy)")]
    public void CopyingEndsAnOutage(bool withCopyFlag)
    {
        var clipboard = new FakeClipboard { Failure = CreateCannotOpenFailure() };
        var reports = new List<Exception?>();
        var guarded = new GuardedClipboard(clipboard, reports.Add, () => TimeSpan.Zero);
        guarded.GetDataObject();

        // Writing opens the clipboard too, so what the user has just copied can be pasted straight away.
        var data = new DataObject();
        clipboard.Failure = null;
        clipboard.DataObject = data;

        if (withCopyFlag)
        {
            guarded.SetDataObject("element", true);
        }
        else
        {
            guarded.SetDataObject("element");
        }

        Assert.AreSame(data, guarded.GetDataObject());
        Assert.HasCount(2, reports);
        Assert.IsNull(reports[1]);
    }

    [TestMethod]
    public void CopyThatFailsLeavesTheOutageInPlace()
    {
        var clipboard = new FakeClipboard { Failure = CreateCannotOpenFailure() };
        var reports = new List<Exception?>();
        var guarded = new GuardedClipboard(clipboard, reports.Add, () => TimeSpan.Zero);
        guarded.GetDataObject();

        // A write that fails has not opened the clipboard either, so it shows nothing about the outage.
        Assert.ThrowsExactly<COMException>(() => guarded.SetDataObject("element"));
        Assert.ThrowsExactly<COMException>(() => guarded.SetDataObject("element", true));

        // The retry delay goes on: a read inside it still answers an empty clipboard without asking.
        Assert.IsEmpty(guarded.GetDataObject().GetFormats());
        Assert.AreEqual(1, clipboard.Opens);
        Assert.HasCount(1, reports);
    }

    private static COMException CreateCannotOpenFailure() => new COMException("OpenClipboard Failed", _ClipboardCannotBeOpened);

    // Behaves like Visual Studio's OleClipboard over WPF: ContainsData asks Windows whether a format is available
    // without opening the clipboard, so a clipboard that cannot be opened fails only the other reads and the writes.
    private sealed class FakeClipboard : IClipboard
    {
        public Exception? Failure { get; set; }

        public Exception? ContainsDataFailure { get; set; }

        public bool ContainsDataResult { get; set; }

        public object? Data { get; set; }

        public IDataObject? DataObject { get; set; }

        public bool IsCurrentResult { get; set; }

        public int Questions { get; private set; }

        public int Opens { get; private set; }

        public List<string> Written { get; } = [];

        public bool ContainsData(string format)
        {
            Questions++;

            if (ContainsDataFailure is not null)
            {
                throw ContainsDataFailure;
            }

            return ContainsDataResult;
        }

        public object GetData(string format)
        {
            Open();
            return Data!;
        }

        public IDataObject GetDataObject()
        {
            Open();
            return DataObject!;
        }

        public bool IsCurrent(IDataObject data)
        {
            Open();
            return IsCurrentResult;
        }

        public void SetDataObject(object data)
        {
            ThrowIfFailing();
            Written.Add((string)data);
        }

        public void SetDataObject(object data, bool copy)
        {
            ThrowIfFailing();
            Written.Add(copy ? $"{data} (copy)" : (string)data);
        }

        private void Open()
        {
            Opens++;
            ThrowIfFailing();
        }

        private void ThrowIfFailing()
        {
            if (Failure is not null)
            {
                throw Failure;
            }
        }
    }
}
