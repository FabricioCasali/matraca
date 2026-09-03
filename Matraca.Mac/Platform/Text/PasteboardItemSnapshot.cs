namespace Matraca.Mac.Platform.Text;

internal sealed record PasteboardItemSnapshot(
    IReadOnlyList<KeyValuePair<string, byte[]>> DataByType);
