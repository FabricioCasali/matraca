using Xunit;

namespace Matraca.Core.Tests;

public sealed class AiCostEstimatorTests
{
    [Theory]
    [InlineData(null, 0, 0, 0)]
    [InlineData(0, null, 0, 0)]
    [InlineData(0, 0, null, 0)]
    [InlineData(0, 0, 0, null)]
    [InlineData(-1, 0, 0, 0)]
    public void MissingOrInvalidBillableTokensAreNotFree(int? prompt, int? hit, int? miss, int? output)
    {
        Assert.Null(AiCostEstimator.EstimateUsd("deepseek", "deepseek-v4-flash",
            DateTime.UtcNow, prompt, hit, miss, output));
    }

    [Fact]
    public void CalculationCapturesVersionEvenForRealZeroButNotForUnknownCost()
    {
        var usage = new TextReviewUsage(Guid.NewGuid(), DateTime.UtcNow, "deepseek",
            "deepseek-v4-flash", "test", 0, 0, 0, 0, null, 0);
        var priced = AiCostEstimator.Estimate(usage);
        Assert.Equal(0m, priced.EstimatedCostUsd);
        Assert.Equal(AiCostEstimator.DeepSeekPricingVersion, priced.PricingVersion);
        var unknown = AiCostEstimator.Estimate(usage with { PromptTokens = null });
        Assert.Null(unknown.EstimatedCostUsd);
        Assert.Null(unknown.PricingVersion);
        var other = AiCostEstimator.Estimate(usage with { Provider = "anthropic" });
        Assert.Null(other.EstimatedCostUsd);
        Assert.Null(other.PricingVersion);
    }

    [Theory]
    [InlineData("deepseek-v4-flash", 0.00005832)]
    [InlineData("deepseek-v4-pro", 0.00017512)]
    public void UsesOfficialPeakRatesWithoutDoubleChargingReasoning(
        string model,
        decimal expected)
    {
        decimal? cost = AiCostEstimator.EstimateUsd(
            "deepseek",
            model,
            new DateTime(2026, 9, 7, 2, 0, 0, DateTimeKind.Utc),
            promptTokens: 120,
            promptCacheHitTokens: 80,
            promptCacheMissTokens: 40,
            completionTokens: 30);

        Assert.Equal(expected, cost);
    }

    [Fact]
    public void UsesOffPeakRatesAndChargesUncategorizedInputAsCacheMiss()
    {
        decimal? cost = AiCostEstimator.EstimateUsd(
            "deepseek",
            "deepseek-v4-flash",
            new DateTime(2026, 9, 6, 2, 0, 0, DateTimeKind.Utc),
            promptTokens: 100,
            promptCacheHitTokens: 0,
            promptCacheMissTokens: 0,
            completionTokens: 10);

        Assert.Equal(0.0000286m, cost);
    }

    [Fact]
    public void UnknownProviderOrModelHasNoEstimate()
    {
        DateTime at = new(2026, 9, 6, 2, 0, 0, DateTimeKind.Utc);

        Assert.Null(AiCostEstimator.EstimateUsd("openai-compatible", "custom", at, 1, 0, 1, 1));
        Assert.Null(AiCostEstimator.EstimateUsd("deepseek", "future-model", at, 1, 0, 1, 1));
    }
}
