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

    private static string NewTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "matraca-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
