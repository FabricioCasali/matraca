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

    private static HttpResponseMessage JsonResponse(string json)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
}
