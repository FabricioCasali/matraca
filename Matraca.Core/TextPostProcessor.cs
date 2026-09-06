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

    private readonly Func<string, CancellationToken, Task<TextReviewResult>> _clean;
    private readonly int _timeoutMs;
    private readonly IDisposable? _resource;
    private readonly CancellationTokenSource _disposalCancellation = new();
    private int _disposed;

    public TextPostProcessor(
        Func<string, CancellationToken, Task<string?>> clean,
        int timeoutMs)
        : this(
            async (text, cancellationToken) => new TextReviewResult(
                await clean(text, cancellationToken).ConfigureAwait(false),
                null),
            timeoutMs,
            null)
    {
    }

    private TextPostProcessor(
        Func<string, CancellationToken, Task<TextReviewResult>> clean,
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

        if (config.PostProcessProvider is "openai-compatible" or "deepseek")
            return TryCreateOpenAiCompatible(config);
        if (config.PostProcessProvider != "anthropic")
        {
            Logger.Warn(
                $"Provedor de pos-processamento '{config.PostProcessProvider}' nao suportado; "
                + "seguindo sem revisar o texto.");
            return null;
        }

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
                async (text, cancellationToken) => new TextReviewResult(
                    await RequestAsync(
                        client,
                        model,
                        prompt,
                        text,
                        cancellationToken).ConfigureAwait(false),
                    null),
                config.PostProcessTimeoutMs,
                null);
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
        => (await CleanWithUsageAsync(text, cancellationToken).ConfigureAwait(false)).Text!;

    public async Task<TextReviewResult> CleanWithUsageAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return new TextReviewResult(text, null);
        if (Volatile.Read(ref _disposed) != 0) return new TextReviewResult(text, null);

        Task<TextReviewResult>? request = null;
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
            TextReviewResult result = await request.WaitAsync(cancellation.Token);
            string? cleaned = result.Text?.Trim();

            if (string.IsNullOrEmpty(cleaned))
            {
                Logger.Warn("Pos-processamento devolveu vazio; usando a transcricao original.");
                return new TextReviewResult(text, result.Usage);
            }

            stopwatch.Stop();
            Logger.Info($"[pos] {stopwatch.ElapsedMilliseconds}ms: \"{text}\" -> \"{cleaned}\"");
            return new TextReviewResult(cleaned, result.Usage);
        }
        catch (OperationCanceledException)
        {
            if (request != null) Observe(request);
            if (cancellationToken.IsCancellationRequested || Volatile.Read(ref _disposed) != 0)
                Logger.Info("Pos-processamento cancelado; usando a transcricao original.");
            else
                Logger.Warn($"Pos-processamento estourou {_timeoutMs}ms; usando a transcricao original.");
            return new TextReviewResult(text, null);
        }
        catch (Exception ex)
        {
            Logger.Error("Pos-processamento falhou; usando a transcricao original", ex);
            return new TextReviewResult(text, null);
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
        bool deepSeek = config.PostProcessProvider == "deepseek";
        if (deepSeek && (config.PostProcessModel.Length == 0 || config.PostProcessReasoning.Length == 0))
        {
            Logger.Warn("Pos-processamento DeepSeek aguarda a escolha de modelo e raciocinio.");
            return null;
        }

        string endpointText = deepSeek
            ? OpenAiCompatibleTextReviewer.DeepSeekEndpoint
            : config.PostProcessEndpoint.Length > 0
            ? config.PostProcessEndpoint
            : OpenAiCompatibleTextReviewer.DefaultEndpoint;
        if (!Uri.TryCreate(endpointText, UriKind.Absolute, out Uri? endpoint)
            || endpoint.Scheme is not ("http" or "https")
            || (endpoint.Scheme == "http" && !endpoint.IsLoopback))
        {
            Logger.Warn(
                "Pos-processamento OpenAI-compatible ligado, mas o endpoint e invalido; "
                + "use HTTPS ou HTTP local.");
            return null;
        }

        string key = ResolveKey(
            config.PostProcessApiKey,
            deepSeek ? "DEEPSEEK_API_KEY" : "OPENAI_API_KEY");
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
                config.PostProcessPrompt.Length > 0 ? config.PostProcessPrompt : DefaultPrompt,
                deepSeek ? config.PostProcessReasoning : "",
                disposeHttpClient: true,
                provider: config.PostProcessProvider);
            Logger.Info(
                $"Pos-processamento de texto ligado (provedor {config.PostProcessProvider}, modelo "
                + $"{config.PostProcessModel}, timeout {config.PostProcessTimeoutMs}ms).");
            return new TextPostProcessor(
                reviewer.ReviewWithUsageAsync,
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
