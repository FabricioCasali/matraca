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
    private readonly IDisposable? _resource;
    private readonly CancellationTokenSource _disposalCancellation = new();
    private int _disposed;

    public TextPostProcessor(
        Func<string, CancellationToken, Task<string?>> clean,
        int timeoutMs)
        : this(clean, timeoutMs, null)
    {
    }

    private TextPostProcessor(
        Func<string, CancellationToken, Task<string?>> clean,
        int timeoutMs,
        IDisposable? resource)
    {
        _clean = clean ?? throw new ArgumentNullException(nameof(clean));
        _timeoutMs = Math.Max(1, timeoutMs);
        _resource = resource;
    }

    public static TextPostProcessor? TryCreate(Config config)
    {
        if (!config.PostProcess) return null;

        if (config.PostProcessProvider == "openai-compatible")
            return TryCreateOpenAiCompatible(config);

        var key = ResolveKey(config.PostProcessApiKey, "ANTHROPIC_API_KEY");
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

    public async Task<string> CleanAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;
        if (Volatile.Read(ref _disposed) != 0) return text;

        Task<string?>? request = null;
        try
        {
            using var timeout = new CancellationTokenSource(_timeoutMs);
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                timeout.Token,
                cancellationToken,
                _disposalCancellation.Token);
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            request = Task.Run(() =>
            {
                cancellation.Token.ThrowIfCancellationRequested();
                return _clean(text, cancellation.Token);
            }, CancellationToken.None);
            var cleaned = (await request.WaitAsync(cancellation.Token))?.Trim();

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
            if (request != null) Observe(request);
            if (cancellationToken.IsCancellationRequested || Volatile.Read(ref _disposed) != 0)
                Logger.Info("Pos-processamento cancelado; usando a transcricao original.");
            else
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
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _disposalCancellation.Cancel();
            _resource?.Dispose();
        }
    }

    private static TextPostProcessor? TryCreateOpenAiCompatible(Config config)
    {
        string endpointText = config.PostProcessEndpoint.Length > 0
            ? config.PostProcessEndpoint
            : OpenAiCompatibleTextReviewer.DefaultEndpoint;
        if (!Uri.TryCreate(endpointText, UriKind.Absolute, out Uri? endpoint)
            || endpoint.Scheme is not ("http" or "https"))
        {
            Logger.Warn("Pos-processamento OpenAI-compatible ligado, mas o endpoint e invalido.");
            return null;
        }

        string key = ResolveKey(config.PostProcessApiKey, "OPENAI_API_KEY");
        if (key.Length == 0 && !endpoint.IsLoopback)
        {
            Logger.Warn("Pos-processamento OpenAI-compatible remoto ligado, mas sem chave de API.");
            return null;
        }

        try
        {
            var reviewer = new OpenAiCompatibleTextReviewer(
                new HttpClient { Timeout = Timeout.InfiniteTimeSpan },
                endpoint,
                key,
                config.PostProcessModel,
                config.PostProcessPrompt.Length > 0 ? config.PostProcessPrompt : DefaultPrompt);
            Logger.Info(
                $"Pos-processamento de texto ligado (provedor openai-compatible, modelo "
                + $"{config.PostProcessModel}, timeout {config.PostProcessTimeoutMs}ms).");
            return new TextPostProcessor(
                reviewer.ReviewAsync,
                config.PostProcessTimeoutMs,
                reviewer);
        }
        catch (Exception ex)
        {
            Logger.Error("Falha ao iniciar o pos-processamento OpenAI-compatible; seguindo sem ele", ex);
            return null;
        }
    }

    private static string ResolveKey(string configured, string environmentVariable)
        => configured.Length > 0
            ? configured
            : Environment.GetEnvironmentVariable(environmentVariable) ?? "";

    private static void Observe(Task task)
        => _ = task.ContinueWith(
            completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

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
