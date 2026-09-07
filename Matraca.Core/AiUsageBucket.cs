namespace Matraca.Core;

public sealed record AiUsageBucket(
    DateOnly Day,
    string Provider,
    string Model,
    long Requests,
    long? PromptTokens,
    long? PromptCacheHitTokens,
    long? PromptCacheMissTokens,
    long? CompletionTokens,
    long? ReasoningTokens,
    long? TotalTokens,
    decimal? EstimatedCostUsd = null,
    int? PricedRequests = null,
    int? UnpricedRequests = null)
{
    // Only versions actually recorded at calculation time; absence is not today's price.
    public string[] PricingVersions { get; init; } = [];
}
