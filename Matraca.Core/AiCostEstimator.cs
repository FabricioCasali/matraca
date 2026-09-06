namespace Matraca.Core;

public static class AiCostEstimator
{
    // Snapshot of https://api-docs.deepseek.com/quick_start/pricing.
    public const string DeepSeekPricingVersion = "2026-09-06";

    public static decimal? EstimateUsd(
        string provider,
        string model,
        DateTime at,
        int promptTokens,
        int promptCacheHitTokens,
        int promptCacheMissTokens,
        int completionTokens)
    {
        if (provider != "deepseek") return null;

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
            promptTokens - promptCacheHitTokens - promptCacheMissTokens);
        decimal input = promptCacheHitTokens * rates.Value.cacheHit
            + (promptCacheMissTokens + uncategorizedPrompt) * rates.Value.cacheMiss;
        decimal output = completionTokens * rates.Value.output;
        return decimal.Round((input + output) / 1_000_000m, 10);
    }

    private static bool IsDeepSeekPeak(DateTime at)
    {
        DateTime utc = at.Kind == DateTimeKind.Utc ? at : at.ToUniversalTime();
        bool weekday = utc.DayOfWeek is >= DayOfWeek.Monday and <= DayOfWeek.Friday;
        return weekday && (utc.Hour is >= 1 and < 4 || utc.Hour is >= 6 and < 10);
    }
}
