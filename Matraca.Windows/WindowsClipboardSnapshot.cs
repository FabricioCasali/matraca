using System.Runtime.InteropServices;
using ComDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace Matraca;

internal sealed class WindowsClipboardSnapshot : IDisposable
{
    private ComDataObject? _dataObject;
    private WindowsOleScope? _oleScope;
    private int _restoreAttempted;
    private int _disposed;

    internal WindowsClipboardSnapshot(
        ComDataObject? dataObject,
        uint writtenSequenceNumber,
        WindowsOleScope oleScope)
    {
        _dataObject = dataObject;
        WrittenSequenceNumber = writtenSequenceNumber;
        _oleScope = oleScope;
    }

    internal ComDataObject? DataObject
    {
        get
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            return _dataObject;
        }
    }

    internal uint WrittenSequenceNumber { get; }

    internal WindowsClipboardRestoreResult RestoreIfOwned()
    {
        if (Interlocked.Exchange(ref _restoreAttempted, 1) != 0)
            return WindowsClipboardRestoreResult.OwnershipLost;
        return WindowsClipboard.RestoreIfOwned(this);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        ComDataObject? dataObject = Interlocked.Exchange(ref _dataObject, null);
        if (dataObject != null && Marshal.IsComObject(dataObject))
            Marshal.ReleaseComObject(dataObject);

        Interlocked.Exchange(ref _oleScope, null)?.Dispose();
    }
}
