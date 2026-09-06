namespace Matraca.Core;

public sealed record DictationHistoryEntry(
    DateTime At,
    string Text,
    TextReviewUsage? ReviewUsage = null);
