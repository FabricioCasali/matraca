using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Matraca.Core;

public sealed class OpenAiCompatibleTextReviewer : IDisposable
{
    public const string DefaultEndpoint = "https://api.openai.com/v1/chat/completions";

    private readonly HttpClient _httpClient;
    private readonly Uri _endpoint;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly string _prompt;
    private readonly bool _disposeHttpClient;

    public OpenAiCompatibleTextReviewer(
        HttpClient httpClient,
        Uri endpoint,
        string apiKey,
        string model,
        string prompt,
        bool disposeHttpClient = false)
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
        _disposeHttpClient = disposeHttpClient;
    }

    public async Task<string?> ReviewAsync(string text, CancellationToken cancellationToken)
    {
        string payload = JsonSerializer.Serialize(new
        {
            model = _model,
            messages = new[]
            {
                new { role = "system", content = _prompt },
                new { role = "user", content = text },
            },
        });
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
        if (!root.TryGetProperty("choices", out JsonElement choices)
            || choices.ValueKind != JsonValueKind.Array
            || choices.GetArrayLength() == 0
            || !choices[0].TryGetProperty("message", out JsonElement message)
            || message.ValueKind != JsonValueKind.Object)
            return null;
        if (message.TryGetProperty("refusal", out JsonElement refusal)
            && refusal.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(refusal.GetString()))
            return null;
        return message.TryGetProperty("content", out JsonElement content)
            && content.ValueKind == JsonValueKind.String
                ? content.GetString()
                : null;
    }

    public void Dispose()
    {
        if (_disposeHttpClient) _httpClient.Dispose();
    }
}
