using Xunit;

namespace Matraca.Core.Tests;

public sealed class DictationHistoryTests
{
    [Fact]
    public void AddTrimsOrdersAndLimitsPersistedEntries()
    {
        var home = NewTemporaryDirectory();
        try
        {
            var paths = AppPaths.ForMac(home);
            var times = new Queue<DateTime>(
            [
                new DateTime(2026, 9, 2, 10, 0, 0),
                new DateTime(2026, 9, 2, 10, 1, 0),
                new DateTime(2026, 9, 2, 10, 2, 0),
            ]);
            var history = new DictationHistory(paths, 2, () => times.Dequeue());

            history.Add("  first  ");
            history.Add("second");
            history.Add("third");
            history.Add("   ");

            var entries = history.Snapshot();
            Assert.Equal(["third", "second"], entries.Select(entry => entry.Text));
            Assert.Equal(new DateTime(2026, 9, 2, 10, 2, 0), entries[0].At);
            Assert.Equal(["third", "second"],
                new DictationHistory(paths, 2).Snapshot().Select(entry => entry.Text));
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }

    [Fact]
    public void CorruptJsonIsIgnoredAndClearRemovesTheFile()
    {
        var home = NewTemporaryDirectory();
        try
        {
            var paths = AppPaths.ForMac(home);
            Directory.CreateDirectory(paths.DataDirectory);
            File.WriteAllText(paths.HistoryFile, "not-json");

            var history = new DictationHistory(paths, 10);
            Assert.Empty(history.Snapshot());

            history.Add("kept briefly");
            Assert.True(File.Exists(paths.HistoryFile));
            history.Clear();

            Assert.Empty(history.Snapshot());
            Assert.False(File.Exists(paths.HistoryFile));
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }

    [Fact]
    public void FailedAtomicReplaceLeavesThePreviousFileIntact()
    {
        var home = NewTemporaryDirectory();
        try
        {
            var paths = AppPaths.ForMac(home);
            var history = new DictationHistory(paths, 10);
            history.Add("first");
            string committed = File.ReadAllText(paths.HistoryFile);
            string? staged = null;
            var interrupted = new DictationHistory(
                paths,
                10,
                replaceFile: (temporary, destination) =>
                {
                    Assert.Equal(paths.HistoryFile, destination);
                    staged = File.ReadAllText(temporary);
                    throw new IOException("interrupted before replace");
                });

            interrupted.Add("second");

            Assert.Equal(committed, File.ReadAllText(paths.HistoryFile));
            Assert.NotNull(staged);
            Assert.Contains("second", staged);
            Assert.Equal(["first"],
                new DictationHistory(paths, 10).Snapshot().Select(entry => entry.Text));
            Assert.Empty(Directory.EnumerateFiles(
                paths.DataDirectory,
                $".{Path.GetFileName(paths.HistoryFile)}.*.tmp"));
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }

    [Fact]
    public void ReducingMaximumPersistsTheNewestEntriesImmediately()
    {
        var home = NewTemporaryDirectory();
        try
        {
            var paths = AppPaths.ForMac(home);
            var history = new DictationHistory(paths, 10);
            history.Add("first");
            history.Add("second");
            history.Add("third");

            history.SetMaximumItems(2);

            Assert.Equal(["third", "second"], history.Snapshot().Select(entry => entry.Text));
            Assert.Equal(["third", "second"],
                new DictationHistory(paths, 10).Snapshot().Select(entry => entry.Text));
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }

    [Fact]
    public void RemoveDeletesOnlyTheMatchingPersistedEntry()
    {
        var home = NewTemporaryDirectory();
        try
        {
            var paths = AppPaths.ForMac(home);
            DateTime firstAt = new(2026, 9, 4, 10, 0, 0);
            DateTime secondAt = firstAt.AddMinutes(1);
            var times = new Queue<DateTime>([firstAt, secondAt]);
            var history = new DictationHistory(paths, 10, () => times.Dequeue());
            history.Add("first");
            history.Add("second");

            Assert.False(history.Remove(firstAt, "other"));
            Assert.True(history.Remove(firstAt, "first"));
            Assert.False(history.Remove(firstAt, "first"));
            Assert.Equal(["second"], history.Snapshot().Select(entry => entry.Text));
            Assert.Equal(["second"],
                new DictationHistory(paths, 10).Snapshot().Select(entry => entry.Text));
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }

    private static string NewTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "matraca-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
