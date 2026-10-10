using Xunit;

namespace Matraca.Core.Tests;

public sealed class CopyLastTranscriptionHistoryTests
{
    [Fact]
    public void NewestEntryHasTheCompleteStoredTextNotThePreviewOrOlderEntry()
    {
        WithHistory(history =>
        {
            history.Add("Anterior");
            history.Add("  Árvore 🌿\nSegunda linha 🎙️\nFim da transcrição  ");

            var entries = history.Snapshot();
            Assert.Equal("Árvore 🌿\nSegunda linha 🎙️\nFim da transcrição", entries[0].Text);
            Assert.Equal("Anterior", entries[1].Text);
        });
    }

    [Fact]
    public void FirstEntryIsTheNewestEvenWhenItsTimestampIsEarlier()
    {
        WithPaths(paths =>
        {
            DateTime later = new(2026, 10, 10, 12, 0, 0);
            DateTime earlier = later.AddHours(-1);
            var times = new Queue<DateTime>([later, earlier]);
            var history = new DictationHistory(paths, 10, () => times.Dequeue());

            history.Add("anterior");
            history.Add("último trecho");

            Assert.Equal(["último trecho", "anterior"],
                history.Snapshot().Select(entry => entry.Text));
        });
    }

    [Fact]
    public void RestartLoadsTheLastStoredEntryWithoutNewDictation()
    {
        WithPaths(paths =>
        {
            var history = new DictationHistory(paths, 10);
            history.Add("primeiro");
            history.Add("último revisado");

            var reopened = new DictationHistory(paths, 10);

            Assert.Equal("último revisado", reopened.Snapshot()[0].Text);
        });
    }

    [Fact]
    public void FinalStoredTextIsIndependentOfReviewMetadata()
    {
        WithHistory(history =>
        {
            var usage = new TextReviewUsage(
                Guid.NewGuid(), new DateTime(2026, 10, 10, 12, 0, 0),
                "provider", "model", "request", 120, 80, 40, 30, 10, 150);
            history.Add("texto original");
            history.Add("texto final revisado", usage);

            var entries = history.Snapshot();
            Assert.Equal("texto final revisado", entries[0].Text);
            Assert.Equal(usage, entries[0].ReviewUsage);
            Assert.Equal("texto original", entries[1].Text);
            Assert.Null(entries[1].ReviewUsage);
        });
    }

    [Fact]
    public void RemovingNewestLeavesTheNextEntryAndClearingLeavesNone()
    {
        WithHistory(history =>
        {
            history.Add("anterior");
            history.Add("mais recente");
            var last = history.Snapshot()[0];

            Assert.True(history.Remove(last.At, last.Text));
            Assert.Equal("anterior", Assert.Single(history.Snapshot()).Text);

            history.Clear();
            Assert.Empty(history.Snapshot());
        });
    }

    private static void WithHistory(Action<DictationHistory> assertion) =>
        WithPaths(paths => assertion(new DictationHistory(paths, 10)));

    private static void WithPaths(Action<AppPaths> assertion)
    {
        string home = Path.Combine(Path.GetTempPath(), "matraca-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(home);
        try
        {
            assertion(AppPaths.ForMac(home));
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }
}
