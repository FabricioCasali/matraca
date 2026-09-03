using System.Text;
using Matraca.Mac.Platform.Text;

namespace Matraca.Core.Tests;

internal sealed class FakePasteboard : IPasteboard
{
    private const string StringType = "public.utf8-plain-text";
    private const string MarkerType = "io.github.fabriciocasali.matraca.delivery-marker";

    public FakePasteboard(PasteboardSnapshot initial) => Current = Clone(initial);

    public PasteboardSnapshot Current { get; private set; }
    public bool ChangeBeforeWrite { get; set; }
    public bool FailRestore { get; set; }
    private long ChangeCount { get; set; } = 1;

    public bool TryCapture(out PasteboardSnapshot snapshot, out long changeCount)
    {
        snapshot = Clone(Current);
        changeCount = ChangeCount;
        return true;
    }

    public bool TryWriteText(
        PasteboardSnapshot previous,
        string text,
        byte[] marker,
        long expectedChangeCount,
        out long ownChangeCount)
    {
        if (ChangeBeforeWrite)
            SetUserClipboard(Snapshot("public.data", [9, 8, 7]));
        if (ChangeCount != expectedChangeCount)
        {
            ownChangeCount = 0;
            return false;
        }

        Current = new PasteboardSnapshot([
            new PasteboardItemSnapshot([
                new KeyValuePair<string, byte[]>(StringType, Encoding.UTF8.GetBytes(text)),
                new KeyValuePair<string, byte[]>(MarkerType, marker.ToArray()),
            ]),
        ]);
        ownChangeCount = ++ChangeCount;
        return true;
    }

    public PasteboardRestoreResult RestoreIfOwned(
        PasteboardSnapshot snapshot,
        long ownChangeCount,
        byte[] marker)
    {
        if (ChangeCount != ownChangeCount || !HasMarker(marker))
            return PasteboardRestoreResult.OwnershipLost;
        if (FailRestore) return PasteboardRestoreResult.Failed;
        Current = Clone(snapshot);
        ChangeCount++;
        return PasteboardRestoreResult.Restored;
    }

    public void SetUserClipboard(PasteboardSnapshot snapshot)
    {
        Current = Clone(snapshot);
        ChangeCount++;
    }

    public static PasteboardSnapshot Snapshot(string type, byte[] data)
        => new([
            new PasteboardItemSnapshot([
                new KeyValuePair<string, byte[]>(type, data.ToArray()),
            ]),
        ]);

    private bool HasMarker(byte[] marker)
        => Current.Items.Count == 1
            && Current.Items[0].DataByType.Any(value =>
                value.Key == MarkerType && value.Value.AsSpan().SequenceEqual(marker));

    private static PasteboardSnapshot Clone(PasteboardSnapshot source)
        => new(source.Items.Select(item => new PasteboardItemSnapshot(
            item.DataByType.Select(value =>
                new KeyValuePair<string, byte[]>(value.Key, value.Value.ToArray())).ToArray())).ToArray());
}
