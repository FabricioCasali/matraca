using System.Security.Cryptography;

namespace Matraca.Mac.Platform.Text;

internal sealed class PasteboardTransaction
{
    private const int MarkerBytes = 16;

    private readonly byte[] _marker;
    private readonly long _ownChangeCount;
    private readonly IPasteboard _pasteboard;
    private readonly PasteboardSnapshot _snapshot;
    private int _restored;

    private PasteboardTransaction(
        IPasteboard pasteboard,
        PasteboardSnapshot snapshot,
        byte[] marker,
        long ownChangeCount)
    {
        _pasteboard = pasteboard;
        _snapshot = snapshot;
        _marker = marker;
        _ownChangeCount = ownChangeCount;
    }

    public static bool TryBegin(IPasteboard pasteboard, string text, out PasteboardTransaction? transaction)
    {
        ArgumentNullException.ThrowIfNull(pasteboard);
        ArgumentNullException.ThrowIfNull(text);
        transaction = null;

        if (!pasteboard.TryCapture(out PasteboardSnapshot snapshot, out long changeCount))
            return false;

        byte[] marker = RandomNumberGenerator.GetBytes(MarkerBytes);
        if (!pasteboard.TryWriteText(snapshot, text, marker, changeCount, out long ownChangeCount))
            return false;

        transaction = new PasteboardTransaction(pasteboard, snapshot, marker, ownChangeCount);
        return true;
    }

    public PasteboardRestoreResult Restore()
    {
        if (Interlocked.Exchange(ref _restored, 1) != 0)
            return PasteboardRestoreResult.OwnershipLost;
        return _pasteboard.RestoreIfOwned(_snapshot, _ownChangeCount, _marker);
    }
}
