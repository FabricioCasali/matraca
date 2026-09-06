using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Matraca.Core;

public sealed class DeepSeekBalanceClient
{
    public const string Endpoint = "https://api.deepseek.com/user/balance";

    private readonly HttpClient _httpClient;

    public DeepSeekBalanceClient(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<DeepSeekBalance> GetAsync(
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Configure a chave da DeepSeek antes de consultar o saldo.");

        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        using HttpResponseMessage response = await _httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using JsonDocument document = await JsonDocument
            .ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        JsonElement root = document.RootElement;
        bool available = root.TryGetProperty("is_available", out JsonElement availableElement)
            && availableElement.ValueKind is JsonValueKind.True or JsonValueKind.False
            && availableElement.GetBoolean();
        var balances = new List<DeepSeekBalanceInfo>();
        if (root.TryGetProperty("balance_infos", out JsonElement infos)
            && infos.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement info in infos.EnumerateArray())
            {
                if (info.ValueKind != JsonValueKind.Object) continue;
                balances.Add(new DeepSeekBalanceInfo(
                    ReadString(info, "currency"),
                    ReadDecimal(info, "total_balance"),
                    ReadDecimal(info, "granted_balance"),
                    ReadDecimal(info, "topped_up_balance")));
            }
        }
        return new DeepSeekBalance(available, balances);
    }

    public static async Task<DeepSeekBalance> GetCurrentAsync(
        string configuredApiKey,
        CancellationToken cancellationToken = default)
    {
        string apiKey = configuredApiKey.Length > 0
            ? configuredApiKey
            : Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY") ?? "";
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        return await new DeepSeekBalanceClient(httpClient)
            .GetAsync(apiKey, cancellationToken)
            .ConfigureAwait(false);
    }

    private static string ReadString(JsonElement parent, string property)
        => parent.TryGetProperty(property, out JsonElement value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? ""
                : "";

    private static decimal ReadDecimal(JsonElement parent, string property)
        => decimal.TryParse(
            ReadString(parent, property),
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out decimal value)
                ? value
                : 0;
}
