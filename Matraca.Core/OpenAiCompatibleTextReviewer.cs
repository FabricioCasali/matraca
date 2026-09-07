using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Matraca.Core;

public sealed class OpenAiCompatibleTextReviewer : IDisposable
{
    public const string DefaultEndpoint = "https://api.openai.com/v1/chat/completions";
    public const string DeepSeekEndpoint = "https://api.deepseek.com/chat/completions";

    private readonly HttpClient _httpClient;
    private readonly Uri _endpoint;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly string _prompt;
    private readonly string _reasoning;
    private readonly string _provider;
    private readonly bool _disposeHttpClient;

    public OpenAiCompatibleTextReviewer(
        HttpClient httpClient,
        Uri endpoint,
        string apiKey,
        string model,
        string prompt,
        string reasoning = "",
        bool disposeHttpClient = false,
        string provider = "openai-compatible")
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
        if (_endpoint.Scheme is not ("http" or "https"))
            throw new ArgumentException("Endpoint must use HTTP or HTTPS.", nameof(endpoint));
        if (_endpoint.Scheme == "http" && !_endpoint.IsLoopback)
            throw new ArgumentException("Remote endpoints must use HTTPS.", nameof(endpoint));
        _apiKey = apiKey ?? "";
        _model = string.IsNullOrWhiteSpace(model)
            ? throw new ArgumentException("Model is required.", nameof(model))
            : model.Trim();
        _prompt = string.IsNullOrWhiteSpace(prompt)
            ? throw new ArgumentException("Prompt is required.", nameof(prompt))
            : prompt;
        _reasoning = Config.NormalizePostProcessReasoning(reasoning);
        _provider = string.IsNullOrWhiteSpace(provider) ? "openai-compatible" : provider.Trim();
        _disposeHttpClient = disposeHttpClient;
    }

    public async Task<string?> ReviewAsync(string text, CancellationToken cancellationToken)
        => (await ReviewWithUsageAsync(text, cancellationToken).ConfigureAwait(false)).Text;

    public async Task<TextReviewResult> ReviewWithUsageAsync(
        string text,
        CancellationToken cancellationToken)
    {
        var payloadValues = new Dictionary<string, object>
        {
            ["model"] = _model,
            ["messages"] = new[]
            {
                new { role = "system", content = _prompt },
                new { role = "user", content = text },
            },
        };
        if (_reasoning.Length > 0)
        {
            payloadValues["thinking"] = new { type = _reasoning == "off" ? "disabled" : "enabled" };
            if (_reasoning != "off") payloadValues["reasoning_effort"] = _reasoning;
        }
        string payload = JsonSerializer.Serialize(payloadValues);
        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        if (_apiKey.Length > 0)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        using HttpResponseMessage response = await _httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using JsonDocument document = await JsonDocument.ParseAsync(
                stream,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        JsonElement root = document.RootElement;
        TextReviewUsage? usage = ParseUsage(root, response);
        if (!root.TryGetProperty("choices", out JsonElement choices)
            || choices.ValueKind != JsonValueKind.Array
            || choices.GetArrayLength() == 0
            || !choices[0].TryGetProperty("message", out JsonElement message)
            || message.ValueKind != JsonValueKind.Object)
            return new TextReviewResult(null, usage);
        if (message.TryGetProperty("refusal", out JsonElement refusal)
            && refusal.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(refusal.GetString()))
            return new TextReviewResult(null, usage);
        string? reviewed = message.TryGetProperty("content", out JsonElement content)
            && content.ValueKind == JsonValueKind.String
                ? content.GetString()
                : null;
        return new TextReviewResult(reviewed, usage);
    }

    public void Dispose()
    {
        if (_disposeHttpClient) _httpClient.Dispose();
    }

    private TextReviewUsage? ParseUsage(JsonElement root, HttpResponseMessage response)
    {
        if (!root.TryGetProperty("usage", out JsonElement usage)
            || usage.ValueKind != JsonValueKind.Object)
            return null;

        string model = root.TryGetProperty("model", out JsonElement responseModel)
            && responseModel.ValueKind == JsonValueKind.String
            ? responseModel.GetString() ?? _model
            : _model;
        string requestId = response.Headers.TryGetValues("x-request-id", out IEnumerable<string>? values)
            ? values.FirstOrDefault() ?? ""
            : root.TryGetProperty("id", out JsonElement responseId)
                && responseId.ValueKind == JsonValueKind.String
                ? responseId.GetString() ?? ""
                : "";

        DateTime at = DateTime.Now;
        return AiCostEstimator.Estimate(new TextReviewUsage(
            Guid.NewGuid(),
            at,
            _provider,
            model,
            requestId,
            ReadInt32(usage, "prompt_tokens"),
            ReadInt32(usage, "prompt_cache_hit_tokens"),
            ReadInt32(usage, "prompt_cache_miss_tokens"),
            ReadInt32(usage, "completion_tokens"),
            ReadNestedInt32(usage, "completion_tokens_details", "reasoning_tokens"),
            ReadInt32(usage, "total_tokens")));
    }

    private static int? ReadInt32(JsonElement parent, string property)
        => parent.TryGetProperty(property, out JsonElement value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out int number)
            && number >= 0
                ? number
                : null;

    private static int? ReadNestedInt32(JsonElement parent, string group, string property)
        => parent.TryGetProperty(group, out JsonElement nested)
            && nested.ValueKind == JsonValueKind.Object
                ? ReadInt32(nested, property)
                : null;
}
