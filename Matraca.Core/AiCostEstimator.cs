namespace Matraca.Core;

public static class AiCostEstimator
{
    // Snapshot of https://api-docs.deepseek.com/quick_start/pricing.
    public const string DeepSeekPricingVersion = "2026-09-06";

    public static decimal? EstimateUsd(
        string provider,
        string model,
        DateTime at,
        int? promptTokens,
        int? promptCacheHitTokens,
        int? promptCacheMissTokens,
        int? completionTokens)
    {
        if (provider != "deepseek") return null;
        if (promptTokens is null or < 0 || promptCacheHitTokens is null or < 0
            || promptCacheMissTokens is null or < 0 || completionTokens is null or < 0)
            return null;

        bool peak = IsDeepSeekPeak(at);
        (decimal cacheHit, decimal cacheMiss, decimal output)? rates = model switch
        {
            "deepseek-v4-flash" => peak
                ? (0.014m, 0.44m, 1.32m)
                : (0.007m, 0.22m, 0.66m),
            "deepseek-v4-pro" => peak
                ? (0.044m, 1.32m, 3.96m)
                : (0.022m, 0.66m, 1.98m),
            _ => null,
        };
        if (rates == null) return null;

        int uncategorizedPrompt = Math.Max(
            0,
            promptTokens.Value - promptCacheHitTokens.Value - promptCacheMissTokens.Value);
        decimal input = promptCacheHitTokens.Value * rates.Value.cacheHit
            + ((decimal)promptCacheMissTokens.Value + uncategorizedPrompt) * rates.Value.cacheMiss;
        decimal output = completionTokens.Value * rates.Value.output;
        return decimal.Round((input + output) / 1_000_000m, 10);
    }

    public static TextReviewUsage Estimate(TextReviewUsage usage)
    {
        ArgumentNullException.ThrowIfNull(usage);
        decimal? cost = EstimateUsd(usage.Provider, usage.Model, usage.At, usage.PromptTokens,
            usage.PromptCacheHitTokens, usage.PromptCacheMissTokens, usage.CompletionTokens);
        return usage with
        {
            EstimatedCostUsd = cost,
            PricingVersion = cost.HasValue ? DeepSeekPricingVersion : null,
        };
    }

    private static bool IsDeepSeekPeak(DateTime at)
    {
        DateTime utc = at.Kind == DateTimeKind.Utc ? at : at.ToUniversalTime();
        bool weekday = utc.DayOfWeek is >= DayOfWeek.Monday and <= DayOfWeek.Friday;
        return weekday && (utc.Hour is >= 1 and < 4 || utc.Hour is >= 6 and < 10);
    }
}
