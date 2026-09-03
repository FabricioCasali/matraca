namespace Matraca.Mac.Platform.Text;

internal sealed record PasteboardSnapshot(IReadOnlyList<PasteboardItemSnapshot> Items);
