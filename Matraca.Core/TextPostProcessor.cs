using Anthropic;
using Anthropic.Models.Messages;

namespace Matraca.Core;

public sealed class TextPostProcessor : IDisposable
{
    public const string DefaultPrompt =
        "Você recebe a transcrição bruta de um ditado por voz. Devolva o MESMO texto com "
      + "pontuação, acentuação e capitalização corretas, removendo apenas hesitações e "
      + "muletas de fala (\"é...\", \"tipo\", \"né\", \"então assim\", repetições de gaguejo).\n"
      + "NÃO reescreva, não resuma, não traduza, não formate em markdown e não adicione nada. "
      + "Preserve o vocabulário, os termos técnicos e o tom de quem falou.\n"
      + "Responda APENAS com o texto corrigido, sem aspas, sem comentários e sem preâmbulo.";

    private readonly Func<string, CancellationToken, Task<string?>> _clean;
    private readonly int _timeoutMs;

    public TextPostProcessor(
        Func<string, CancellationToken, Task<string?>> clean,
        int timeoutMs)
    {
        _clean = clean ?? throw new ArgumentNullException(nameof(clean));
        _timeoutMs = Math.Max(1, timeoutMs);
    }

    public static TextPostProcessor? TryCreate(Config config)
    {
        if (!config.PostProcess) return null;

        var key = config.PostProcessApiKey.Length > 0
            ? config.PostProcessApiKey
            : Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") ?? "";
        if (key.Length == 0)
        {
            Logger.Warn("Pos-processamento ligado, mas sem chave de API (postProcessApiKey ou "
                      + "ANTHROPIC_API_KEY). Seguindo sem limpar o texto.");
            return null;
        }

        try
        {
            var client = new AnthropicClient { ApiKey = key };
            var model = config.PostProcessModel;
            var prompt = config.PostProcessPrompt.Length > 0
                ? config.PostProcessPrompt
                : DefaultPrompt;
            Logger.Info($"Pos-processamento de texto ligado (modelo {model}, timeout {config.PostProcessTimeoutMs}ms).");
            return new TextPostProcessor(
                (text, cancellationToken) => RequestAsync(
                    client,
                    model,
                    prompt,
                    text,
                    cancellationToken),
                config.PostProcessTimeoutMs);
        }
        catch (Exception ex)
        {
            Logger.Error("Falha ao iniciar o pos-processamento; seguindo sem ele", ex);
            return null;
        }
    }

    public async Task<string> CleanAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        try
        {
            using var timeout = new CancellationTokenSource(_timeoutMs);
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var cleaned = (await _clean(text, timeout.Token).WaitAsync(timeout.Token))?.Trim();

            if (string.IsNullOrEmpty(cleaned))
            {
                Logger.Warn("Pos-processamento devolveu vazio; usando a transcricao original.");
                return text;
            }

            stopwatch.Stop();
            Logger.Info($"[pos] {stopwatch.ElapsedMilliseconds}ms: \"{text}\" -> \"{cleaned}\"");
            return cleaned;
        }
        catch (OperationCanceledException)
        {
            Logger.Warn($"Pos-processamento estourou {_timeoutMs}ms; usando a transcricao original.");
            return text;
        }
        catch (Exception ex)
        {
            Logger.Error("Pos-processamento falhou; usando a transcricao original", ex);
            return text;
        }
    }

    public void Dispose()
    {
        // AnthropicClient does not expose resources to dispose.
    }

    private static async Task<string?> RequestAsync(
        AnthropicClient client,
        string model,
        string prompt,
        string text,
        CancellationToken cancellationToken)
    {
        var response = await client.Messages.Create(new MessageCreateParams
        {
            Model = model,
            MaxTokens = 4096,
            Thinking = new ThinkingConfigAdaptive(),
            OutputConfig = new OutputConfig { Effort = Effort.Low },
            System = new List<TextBlockParam> { new() { Text = prompt } },
            Messages = [new() { Role = Role.User, Content = text }],
        }, cancellationToken: cancellationToken);

        if (response.StopReason == "refusal")
        {
            Logger.Warn("Pos-processamento recusado pelo modelo; usando a transcricao original.");
            return null;
        }

        return string.Concat(
            response.Content.Select(block => block.Value).OfType<TextBlock>().Select(block => block.Text));
    }
}
