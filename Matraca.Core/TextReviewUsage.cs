namespace Matraca.Core;

public sealed record TextReviewUsage(
    Guid Id,
    DateTime At,
    string Provider,
    string Model,
    string RequestId,
    int PromptTokens,
    int PromptCacheHitTokens,
    int PromptCacheMissTokens,
    int CompletionTokens,
    int ReasoningTokens,
    int TotalTokens,
    decimal? EstimatedCostUsd = null);
