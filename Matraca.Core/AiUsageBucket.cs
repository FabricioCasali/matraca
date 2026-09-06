namespace Matraca.Core;

public sealed record AiUsageBucket(
    DateOnly Day,
    string Provider,
    string Model,
    long Requests,
    long PromptTokens,
    long PromptCacheHitTokens,
    long PromptCacheMissTokens,
    long CompletionTokens,
    long ReasoningTokens,
    long TotalTokens);
