using System.Text.Json;
using Xunit;

namespace Matraca.Core.Tests;

public sealed class AiUsageLedgerTests
{
    [Fact]
    public void UnknownCostAndRealZeroPersistDistinctlyWithCoverage()
    {
        string home = NewTemporaryDirectory();
        try
        {
            AppPaths paths = AppPaths.ForMac(home);
            var ledger = new AiUsageLedger(paths);
            var usage = NewUsage() with { EstimatedCostUsd = null, PromptTokens = null };
            Assert.True(ledger.Add(usage));
            var unknown = Assert.Single(new AiUsageLedger(paths).Snapshot());
            Assert.Null(unknown.EstimatedCostUsd);
            Assert.Null(unknown.PromptTokens);
            Assert.Equal(0, unknown.PricedRequests);
            Assert.Equal(1, unknown.UnpricedRequests);
            Assert.Empty(unknown.PricingVersions);
            var subtotal = AiUsageSummary.Create([unknown]);
            Assert.Null(subtotal.EstimatedCostUsd);
            Assert.True(subtotal.CoverageKnown);
            Assert.Equal(1, subtotal.UnpricedRequests);

            Assert.True(ledger.Add(usage with
            {
                EstimatedCostUsd = 0m, PricingVersion = "zero-price", Model = "zero-model",
                PromptTokens = 0,
            }));
            var zero = new AiUsageLedger(paths).Snapshot().Single(item => item.Model == "zero-model");
            Assert.Equal(0m, zero.EstimatedCostUsd);
            Assert.Equal(0, zero.PromptTokens);
            Assert.Equal(1, zero.PricedRequests);
            Assert.Equal(0, zero.UnpricedRequests);
            Assert.Equal(["zero-price"], zero.PricingVersions);
            using var json = JsonDocument.Parse(File.ReadAllText(paths.AiUsageFile));
            Assert.Equal(JsonValueKind.Null, json.RootElement[0].GetProperty("EstimatedCostUsd").ValueKind);
            Assert.Equal(JsonValueKind.Number, json.RootElement[1].GetProperty("EstimatedCostUsd").ValueKind);
        }
        finally { Directory.Delete(home, recursive: true); }
    }

    [Theory]
    [InlineData("0", "0")]
    [InlineData("0.25", "0.25")]
    [InlineData("null", null)]
    public void LegacyCoverageIsNeverReconstructedEvenAfterNewUsage(string costJson, string? expectedCost)
    {
        string home = NewTemporaryDirectory();
        try
        {
            AppPaths paths = AppPaths.ForMac(home);
            Directory.CreateDirectory(paths.DataDirectory);
            File.WriteAllText(paths.AiUsageFile, $$"""
                [{"Day":"2026-09-06","Provider":"deepseek","Model":"deepseek-v4-flash",
                  "Requests":3,"PromptTokens":120,"PromptCacheHitTokens":80,"PromptCacheMissTokens":40,
                  "CompletionTokens":30,"ReasoningTokens":10,"TotalTokens":150,"EstimatedCostUsd":{{costJson}}}]
                """);
            var ledger = new AiUsageLedger(paths);
            var legacy = Assert.Single(ledger.Snapshot());
            Assert.Equal(expectedCost == null ? (decimal?)null
                : decimal.Parse(expectedCost, System.Globalization.CultureInfo.InvariantCulture), legacy.EstimatedCostUsd);
            Assert.Null(legacy.PricedRequests);
            Assert.Null(legacy.UnpricedRequests);
            Assert.Empty(legacy.PricingVersions);
            Assert.False(AiUsageSummary.Create([legacy]).CoverageKnown);

            Assert.True(ledger.Add(NewUsage() with { PricingVersion = "new-table" }));
            var updated = Assert.Single(new AiUsageLedger(paths).Snapshot());
            Assert.Null(updated.PricedRequests);
            Assert.Null(updated.UnpricedRequests);
            Assert.Equal(["new-table"], updated.PricingVersions);
            Assert.Equal((legacy.EstimatedCostUsd ?? 0) + 0.0001m, updated.EstimatedCostUsd);
            var summary = AiUsageSummary.Create([updated]);
            Assert.False(summary.CoverageKnown);
            Assert.Null(summary.PricedRequests);
            Assert.Null(summary.UnpricedRequests);
        }
        finally { Directory.Delete(home, recursive: true); }
    }

    [Fact]
    public void DailyBucketKeepsMultipleVersionsAndKnownSubtotalAcrossReloads()
    {
        string home = NewTemporaryDirectory();
        try
        {
            AppPaths paths = AppPaths.ForMac(home);
            var ledger = new AiUsageLedger(paths);
            Assert.True(ledger.Add(NewUsage() with { PricingVersion = "table-a" }));
            ledger = new AiUsageLedger(paths);
            Assert.True(ledger.Add(NewUsage() with { PricingVersion = "table-b" }));
            Assert.True(ledger.Add(NewUsage() with { PricingVersion = "table-a" }));
            Assert.True(ledger.Add(NewUsage() with { EstimatedCostUsd = null, ReasoningTokens = null }));
            var bucket = Assert.Single(new AiUsageLedger(paths).Snapshot());
            Assert.Equal(4, bucket.Requests);
            Assert.Equal(3, bucket.PricedRequests);
            Assert.Equal(1, bucket.UnpricedRequests);
            Assert.Equal(0.0003m, bucket.EstimatedCostUsd);
            Assert.Null(bucket.ReasoningTokens);
            Assert.Equal(["table-a", "table-b"], bucket.PricingVersions);
            var summary = AiUsageSummary.Create([bucket]);
            Assert.True(summary.CoverageKnown);
            Assert.Equal(0.0003m, summary.EstimatedCostUsd);
            Assert.Equal(1, summary.UnpricedRequests);
            Assert.Equal(["table-a", "table-b"], summary.PricingVersions);
            ledger.Snapshot()[0].PricingVersions[0] = "mutated";
            Assert.Equal(["table-a", "table-b"], ledger.Snapshot()[0].PricingVersions);
        }
        finally { Directory.Delete(home, recursive: true); }
    }

    [Fact]
    public void SummaryRequiresBothCoverageCountersFromEveryBucket()
    {
        var known = new AiUsageBucket(new DateOnly(2026, 9, 6), "deepseek", "model",
            2, 0, 0, 0, 0, 0, 0, 0.25m, 1, 1) { PricingVersions = ["a"] };
        var legacy = known with { PricedRequests = null, UnpricedRequests = null, EstimatedCostUsd = null };
        foreach (var incomplete in new[] { legacy, known with { UnpricedRequests = null }, known with { PricedRequests = null } })
        {
            var summary = AiUsageSummary.Create([known, incomplete]);
            Assert.False(summary.CoverageKnown);
            Assert.Null(summary.PricedRequests);
            Assert.Null(summary.UnpricedRequests);
        }
        Assert.Equal(0.25m, AiUsageSummary.Create([known, legacy]).EstimatedCostUsd);
        var empty = AiUsageSummary.Create([]);
        Assert.True(empty.CoverageKnown);
        Assert.Equal(0, empty.Requests);
        Assert.Equal(0, empty.PricedRequests);
        Assert.Equal(0, empty.UnpricedRequests);
    }

    [Fact]
    public void SummaryDistinguishesNoKnownCostFromARealZero()
    {
        var unpriced = new AiUsageBucket(new DateOnly(2026, 9, 7), "other", "model",
            1, 10, 0, 10, 5, 0, 15, null, 0, 1);
        Assert.Null(AiUsageSummary.Create([unpriced]).EstimatedCostUsd);
        var zero = unpriced with { EstimatedCostUsd = 0m, PricedRequests = 1, UnpricedRequests = 0 };
        Assert.Equal(0m, AiUsageSummary.Create([zero]).EstimatedCostUsd);
        Assert.Equal(0m, AiUsageSummary.Create([unpriced, zero]).EstimatedCostUsd);
    }

    [Fact]
    public void HistoryRemovalTruncationAndClearDoNotChangeLedgerOrPricingVersion()
    {
        string home = NewTemporaryDirectory();
        try
        {
            AppPaths paths = AppPaths.ForMac(home);
            var usage = NewUsage() with { PricingVersion = "old-table" };
            var ledger = new AiUsageLedger(paths);
            Assert.True(ledger.Add(usage));
            string committed = File.ReadAllText(paths.AiUsageFile);
            var history = new DictationHistory(paths, 10);
            history.Add("first", usage);
            Assert.Equal("old-table", Assert.Single(new DictationHistory(paths, 10).Snapshot()).ReviewUsage!.PricingVersion);
            var entry = Assert.Single(history.Snapshot());
            Assert.True(history.Remove(entry.At, entry.Text));
            history.Add("second", usage);
            history.Add("third", usage);
            history.SetMaximumItems(1);
            history.Clear();
            Assert.Empty(new DictationHistory(paths, 10).Snapshot());
            Assert.Equal(committed, File.ReadAllText(paths.AiUsageFile));
            Assert.Equal(1, Assert.Single(new AiUsageLedger(paths).Snapshot()).Requests);
        }
        finally { Directory.Delete(home, recursive: true); }
    }

    [Fact]
    public void LegacyUsageDetailDoesNotAcquireCurrentPricingVersion()
    {
        var usage = JsonSerializer.Deserialize<TextReviewUsage>("""
            {"Id":"22b59bac-0560-455d-90ea-a39ee79e7ae4","At":"2026-09-06T10:30:00",
             "Provider":"deepseek","Model":"deepseek-v4-flash","RequestId":"old",
             "PromptTokens":0,"CompletionTokens":0,"EstimatedCostUsd":0}
            """)!;
        Assert.Equal(0m, usage.EstimatedCostUsd);
        Assert.Null(usage.PricingVersion);
        Assert.Null(usage.ReasoningTokens);
        var reloaded = JsonSerializer.Deserialize<TextReviewUsage>(JsonSerializer.Serialize(usage))!;
        Assert.Null(reloaded.PricingVersion);
    }

    [Fact]
    public void MissingLegacyCostAndCoverageFieldsStayNull()
    {
        var bucket = JsonSerializer.Deserialize<AiUsageBucket>("""
            {"Day":"2026-09-06","Provider":"deepseek","Model":"deepseek-v4-flash","Requests":1}
            """)!;
        Assert.Null(bucket.EstimatedCostUsd);
        Assert.Null(bucket.PricedRequests);
        Assert.Null(bucket.UnpricedRequests);
        Assert.Null(bucket.PromptTokens);
        Assert.Empty(bucket.PricingVersions);
        var summary = AiUsageSummary.Create([bucket]);
        Assert.False(summary.CoverageKnown);
        Assert.Null(summary.EstimatedCostUsd);
    }

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
            Assert.Equal(0.0002m, bucket.EstimatedCostUsd);
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
            var rolledBack = Assert.Single(interrupted.Snapshot());
            Assert.Equal(1, rolledBack.PricedRequests);
            Assert.Equal(0, rolledBack.UnpricedRequests);
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
        150,
        0.0001m);

    private static string NewTemporaryDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "matraca-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
