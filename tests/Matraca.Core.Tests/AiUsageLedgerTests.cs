using Xunit;

namespace Matraca.Core.Tests;

public sealed class AiUsageLedgerTests
{
    [Fact]
    public void AddPersistsUsageWithoutReviewedText()
    {
        string home = NewTemporaryDirectory();
        try
        {
            AppPaths paths = AppPaths.ForMac(home);
            TextReviewUsage usage = NewUsage();
            var ledger = new AiUsageLedger(paths);

            Assert.True(ledger.Add(usage));
            Assert.True(ledger.Add(usage with { Id = Guid.NewGuid() }));

            AiUsageBucket bucket = Assert.Single(new AiUsageLedger(paths).Snapshot());
            Assert.Equal(DateOnly.FromDateTime(usage.At), bucket.Day);
            Assert.Equal(usage.Provider, bucket.Provider);
            Assert.Equal(usage.Model, bucket.Model);
            Assert.Equal(2, bucket.Requests);
            Assert.Equal(usage.TotalTokens * 2, bucket.TotalTokens);
            string persisted = File.ReadAllText(paths.AiUsageFile);
            Assert.DoesNotContain("dictated text", persisted, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }

    [Fact]
    public void FailedAtomicReplaceKeepsPreviousUsage()
    {
        string home = NewTemporaryDirectory();
        try
        {
            AppPaths paths = AppPaths.ForMac(home);
            var ledger = new AiUsageLedger(paths);
            TextReviewUsage first = NewUsage();
            Assert.True(ledger.Add(first));
            string committed = File.ReadAllText(paths.AiUsageFile);
            var interrupted = new AiUsageLedger(
                paths,
                (_, _) => throw new IOException("interrupted before replace"));

            Assert.False(interrupted.Add(NewUsage() with { Id = Guid.NewGuid() }));

            Assert.Equal(committed, File.ReadAllText(paths.AiUsageFile));
            AiUsageBucket bucket = Assert.Single(new AiUsageLedger(paths).Snapshot());
            Assert.Equal(1, bucket.Requests);
            Assert.Equal(first.TotalTokens, bucket.TotalTokens);
            Assert.Empty(Directory.EnumerateFiles(
                paths.DataDirectory,
                $".{Path.GetFileName(paths.AiUsageFile)}.*.tmp"));
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }

    private static TextReviewUsage NewUsage() => new(
        Guid.Parse("22b59bac-0560-455d-90ea-a39ee79e7ae4"),
        new DateTime(2026, 9, 6, 10, 30, 0),
        "deepseek",
        "deepseek-v4-flash",
        "request-1",
        120,
        80,
        40,
        30,
        10,
        150);

    private static string NewTemporaryDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "matraca-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
