// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.VisualStudio.DesignTools.Utility.Clipboard;

namespace WinUIDesigner.Platform;

/// <summary>
/// The clipboard the designer reads through: a clipboard that cannot be read counts as an empty one.
/// </summary>
/// <remarks>
/// <para>
/// The clipboard cannot be opened while another process holds it or while the session is locked. The reads that
/// open it (<see cref="GetDataObject"/>, <see cref="GetData"/> and <see cref="IsCurrent"/>) then keep the UI thread
/// waiting for about a second in the retries of WPF and fail with <c>CLIPBRD_E_CANT_OPEN</c>. Visual Studio's paste
/// does not survive that: <see cref="Microsoft.VisualStudio.DesignTools.Utility.Data.SafeDataObject.FromClipboard"/>
/// turns the failure into <see langword="null"/>, which <c>PasteCommand.PasteData</c> then uses, and
/// <c>DataObjectPasteHandler.Paste</c> does not catch it. Either way Visual Studio shows the exception in place of
/// the design surface and ends the surface process.
/// </para>
/// <para>
/// <see cref="ContainsData"/>, the question Visual Studio asks on every refresh of its Paste command, does not open
/// the clipboard: WPF answers it with <c>IsClipboardFormatAvailable</c>. So it is never delayed, and its answer does
/// not show that the clipboard can be opened again.
/// </para>
/// <para>
/// Writing is passed through unchanged, because Visual Studio reports a failed copy to the user itself. A write that
/// succeeds ends an outage, because it has opened the clipboard.
/// </para>
/// </remarks>
internal sealed class GuardedClipboard : IClipboard
{
    /// <summary>
    /// How long the reads that open the clipboard answer without asking after the clipboard could not be read.
    /// </summary>
    /// <remarks>
    /// A read that cannot open the clipboard has already kept the UI thread waiting for about a second in the retries
    /// of WPF, and while text is edited in place Visual Studio reads the data object on every refresh of its commands.
    /// </remarks>
    internal static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    private readonly IClipboard _clipboard;
    private readonly Action<Exception?>? _availabilityChanged;
    private readonly Func<TimeSpan> _getTime;

    // The designer uses the clipboard on the UI thread only, so this state is not synchronized.
    private bool _isUnavailable;
    private TimeSpan _retryTime;

    /// <summary>
    /// Initializes a new instance of the <see cref="GuardedClipboard"/> class.
    /// </summary>
    /// <param name="clipboard">The clipboard to read and write.</param>
    /// <param name="availabilityChanged">
    /// Called with the failure when the clipboard stops being readable, and with <see langword="null"/>
    /// when it is readable again.
    /// </param>
    /// <param name="getTime">A monotonic clock; when omitted, the time since this instance was created.</param>
    public GuardedClipboard(IClipboard clipboard, Action<Exception?>? availabilityChanged = null, Func<TimeSpan>? getTime = null)
    {
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        _availabilityChanged = availabilityChanged;

        if (getTime is null)
        {
            var stopwatch = Stopwatch.StartNew();
            getTime = () => stopwatch.Elapsed;
        }

        _getTime = getTime;
    }

    // Within the retry delay, the reads that open the clipboard answer as an empty clipboard without asking.
    private bool IsInRetryDelay => _isUnavailable && _getTime() < _retryTime;

    public bool ContainsData(string format)
    {
        // Windows answers this without opening the clipboard, so it is never delayed and its answer ends no outage.
        try
        {
            return _clipboard.ContainsData(format);
        }
        catch (Exception exception) when (IsClipboardFailure(exception))
        {
            // WPF fails it only when Windows cannot register a format name it has not seen before.
            MarkUnavailable(exception);
            return false;
        }
    }

    public object? GetData(string format)
    {
        // Within the retry delay: an empty clipboard, without waiting for it again.
        if (IsInRetryDelay)
        {
            return null;
        }

        // Otherwise ask: an answer ends an outage, and a failure starts one or makes it last.
        try
        {
            object? data = _clipboard.GetData(format);
            MarkAvailable();
            return data;
        }
        catch (Exception exception) when (IsClipboardFailure(exception))
        {
            MarkUnavailable(exception);
            return null;
        }
    }

    // Never null: some of Visual Studio's paste code uses the result without checking it.
    // With an object that holds no format, pasting ends with the "cannot paste" message.
    public IDataObject GetDataObject()
    {
        // Within the retry delay: an empty clipboard, without waiting for it again.
        if (IsInRetryDelay)
        {
            return new DataObject();
        }

        // Otherwise ask: an answer ends an outage, and a failure starts one or makes it last.
        try
        {
            IDataObject data = _clipboard.GetDataObject();
            MarkAvailable();
            return data;
        }
        catch (Exception exception) when (IsClipboardFailure(exception))
        {
            MarkUnavailable(exception);
            return new DataObject();
        }
    }

    public bool IsCurrent(IDataObject data)
    {
        // Within the retry delay: an empty clipboard, without waiting for it again.
        if (IsInRetryDelay)
        {
            return false;
        }

        // Otherwise ask: an answer ends an outage, and a failure starts one or makes it last.
        try
        {
            bool isCurrent = _clipboard.IsCurrent(data);
            MarkAvailable();
            return isCurrent;
        }
        catch (Exception exception) when (IsClipboardFailure(exception))
        {
            MarkUnavailable(exception);
            return false;
        }
    }

    // A write that succeeds has opened the clipboard, so it ends an outage and its retry delay: what the user has
    // just copied can be pasted straight away.
    public void SetDataObject(object data)
    {
        _clipboard.SetDataObject(data);
        MarkAvailable();
    }

    public void SetDataObject(object data, bool copy)
    {
        _clipboard.SetDataObject(data, copy);
        MarkAvailable();
    }

    // The failures Visual Studio's own guarded read (SafeDataObject.FromClipboard) takes for an unreadable
    // clipboard. COMException, which carries CLIPBRD_E_CANT_OPEN, is an ExternalException.
    private static bool IsClipboardFailure(Exception exception) => exception is ExternalException or UnauthorizedAccessException;

    private void MarkAvailable()
    {
        if (_isUnavailable)
        {
            _isUnavailable = false;
            _availabilityChanged?.Invoke(null);
        }
    }

    private void MarkUnavailable(Exception exception)
    {
        _retryTime = _getTime() + RetryDelay;

        if (!_isUnavailable)
        {
            _isUnavailable = true;
            _availabilityChanged?.Invoke(exception);
        }
    }
}
