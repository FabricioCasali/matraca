using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Matraca.Core.Tests;

public sealed class OpenAiCompatibleTextReviewerTests
{
    [Fact]
    public async Task SendsOnlyConfiguredPromptAndTextAndReturnsMessageContent()
    {
        string? body = null;
        Uri? uri = null;
        string? authorization = null;
        using var http = new HttpClient(new StubHttpMessageHandler((request, _) =>
        {
            uri = request.RequestUri;
            authorization = request.Headers.Authorization?.ToString();
            body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return JsonResponse("""{"choices":[{"message":{"content":"Texto revisto."}}]}""");
        }));
        using var reviewer = new OpenAiCompatibleTextReviewer(
            http,
            new Uri("https://review.example/v1/chat/completions"),
            "secret",
            "model-1",
            "Revise sem reescrever.");

        string? result = await reviewer.ReviewAsync("Texto bruto.", CancellationToken.None);

        Assert.Equal("Texto revisto.", result);
        Assert.Equal("https://review.example/v1/chat/completions", uri?.AbsoluteUri);
        Assert.Equal("Bearer secret", authorization);
        using JsonDocument document = JsonDocument.Parse(body!);
        JsonElement root = document.RootElement;
        Assert.Equal("model-1", root.GetProperty("model").GetString());
        JsonElement messages = root.GetProperty("messages");
        Assert.Equal(2, messages.GetArrayLength());
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("Revise sem reescrever.", messages[0].GetProperty("content").GetString());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());
        Assert.Equal("Texto bruto.", messages[1].GetProperty("content").GetString());
        Assert.DoesNotContain("audio", body, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("{\"choices\":[]}")]
    [InlineData("{\"choices\":[{\"message\":{\"content\":null}}]}")]
    [InlineData("{\"choices\":[{\"message\":{\"content\":\"x\",\"refusal\":\"no\"}}]}")]
    public async Task MissingContentOrRefusalReturnsNull(string responseBody)
    {
        using var http = new HttpClient(new StubHttpMessageHandler(
            (_, _) => JsonResponse(responseBody)));
        using var reviewer = new OpenAiCompatibleTextReviewer(
            http,
            new Uri("http://localhost:11434/v1/chat/completions"),
            "",
            "local-model",
            "Review.");

        Assert.Null(await reviewer.ReviewAsync("raw", CancellationToken.None));
    }

    [Fact]
    public async Task CapturesDeepSeekUsageAndRequestIdentity()
    {
        using var http = new HttpClient(new StubHttpMessageHandler((_, _) =>
        {
            HttpResponseMessage response = JsonResponse("""
                {
                  "id":"completion-1",
                  "model":"deepseek-v4-flash",
                  "choices":[{"message":{"content":"Texto revisto."}}],
                  "usage":{
                    "prompt_tokens":120,
                    "prompt_cache_hit_tokens":80,
                    "prompt_cache_miss_tokens":40,
                    "completion_tokens":30,
                    "completion_tokens_details":{"reasoning_tokens":10},
                    "total_tokens":150
                  }
                }
                """);
            response.Headers.Add("x-request-id", "request-1");
            return response;
        }));
        using var reviewer = new OpenAiCompatibleTextReviewer(
            http,
            new Uri(OpenAiCompatibleTextReviewer.DeepSeekEndpoint),
            "secret",
            "configured-model",
            "Review.",
            provider: "deepseek");

        TextReviewResult result = await reviewer.ReviewWithUsageAsync(
            "Texto bruto.",
            CancellationToken.None);

        Assert.Equal("Texto revisto.", result.Text);
        TextReviewUsage usage = Assert.IsType<TextReviewUsage>(result.Usage);
        Assert.Equal("deepseek", usage.Provider);
        Assert.Equal("deepseek-v4-flash", usage.Model);
        Assert.Equal("request-1", usage.RequestId);
        Assert.Equal(120, usage.PromptTokens);
        Assert.Equal(80, usage.PromptCacheHitTokens);
        Assert.Equal(40, usage.PromptCacheMissTokens);
        Assert.Equal(30, usage.CompletionTokens);
        Assert.Equal(10, usage.ReasoningTokens);
        Assert.Equal(150, usage.TotalTokens);
    }

    [Theory]
    [InlineData("off", "disabled", false)]
    [InlineData("low", "enabled", true)]
    [InlineData("high", "enabled", true)]
    [InlineData("max", "enabled", true)]
    public async Task SendsDeepSeekThinkingMode(string effort, string thinking, bool hasEffort)
    {
        string? body = null;
        using var http = new HttpClient(new StubHttpMessageHandler((request, _) =>
        {
            body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return JsonResponse("""{"choices":[{"message":{"content":"Revisto."}}]}""");
        }));
        using var reviewer = new OpenAiCompatibleTextReviewer(
            http,
            new Uri(OpenAiCompatibleTextReviewer.DeepSeekEndpoint),
            "deepseek-secret",
            "deepseek-v4-flash",
            "Review.",
            effort);

        await reviewer.ReviewAsync("raw", CancellationToken.None);

        using JsonDocument document = JsonDocument.Parse(body!);
        JsonElement root = document.RootElement;
        Assert.Equal(thinking, root.GetProperty("thinking").GetProperty("type").GetString());
        Assert.Equal(hasEffort, root.TryGetProperty("reasoning_effort", out JsonElement value));
        if (hasEffort) Assert.Equal(effort, value.GetString());
    }

    [Fact]
    public void RemoteHttpEndpointIsRejected()
    {
        using var http = new HttpClient(new StubHttpMessageHandler(
            (_, _) => throw new InvalidOperationException("request should not run")));

        Assert.Throws<ArgumentException>(() => new OpenAiCompatibleTextReviewer(
            http,
            new Uri("http://review.example/v1/chat/completions"),
            "secret",
            "model",
            "Review."));
    }

    private static HttpResponseMessage JsonResponse(string json)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
}
