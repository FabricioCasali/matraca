namespace Matraca.Mac.Platform.Text;

internal interface IPasteboard
{
    bool TryCapture(out PasteboardSnapshot snapshot, out long changeCount);

    bool TryWriteText(
        PasteboardSnapshot previous,
        string text,
        byte[] marker,
        long expectedChangeCount,
        out long ownChangeCount);

    PasteboardRestoreResult RestoreIfOwned(
        PasteboardSnapshot snapshot,
        long ownChangeCount,
        byte[] marker);
}
