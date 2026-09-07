namespace Matraca.Core;

public sealed record AiUsageSummary(
    long Requests,
    long? PromptTokens,
    long? PromptCacheHitTokens,
    long? PromptCacheMissTokens,
    long? CompletionTokens,
    long? ReasoningTokens,
    long? TotalTokens,
    decimal? EstimatedCostUsd,
    int? PricedRequests,
    int? UnpricedRequests,
    bool CoverageKnown,
    string[] PricingVersions)
{
    // Cost is the sum of known costs, not the total bill. Coverage describes recorded usage only.
    public static AiUsageSummary Create(IEnumerable<AiUsageBucket> buckets)
    {
        ArgumentNullException.ThrowIfNull(buckets);
        AiUsageBucket[] items = buckets.ToArray();
        bool coverageKnown = items.All(item => item.PricedRequests.HasValue && item.UnpricedRequests.HasValue);
        return new AiUsageSummary(
            items.Sum(item => item.Requests),
            items.Aggregate((long?)0, (sum, item) => sum + item.PromptTokens),
            items.Aggregate((long?)0, (sum, item) => sum + item.PromptCacheHitTokens),
            items.Aggregate((long?)0, (sum, item) => sum + item.PromptCacheMissTokens),
            items.Aggregate((long?)0, (sum, item) => sum + item.CompletionTokens),
            items.Aggregate((long?)0, (sum, item) => sum + item.ReasoningTokens),
            items.Aggregate((long?)0, (sum, item) => sum + item.TotalTokens),
            items.Any(item => item.EstimatedCostUsd.HasValue)
                ? items.Sum(item => item.EstimatedCostUsd ?? 0) : null,
            coverageKnown ? items.Sum(item => item.PricedRequests!.Value) : null,
            coverageKnown ? items.Sum(item => item.UnpricedRequests!.Value) : null,
            coverageKnown,
            items.SelectMany(item => item.PricingVersions ?? [])
                .Distinct(StringComparer.Ordinal)
                .OrderBy(version => version, StringComparer.Ordinal)
                .ToArray());
    }
}
