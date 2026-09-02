using Anthropic;
using Anthropic.Models.Messages;

namespace Matraca;

/// <summary>
/// Limpeza opcional do texto transcrito por um modelo Claude: pontuacao, capitalizacao e
/// remocao de muletas ("é...", "tipo", "né"). Desligado por padrao — quando ligado, o texto
/// so' e' entregue depois da resposta do modelo, entao custa uma ida a' rede.
///
/// REGRA DE OURO: qualquer falha (sem chave, rede fora, timeout, recusa do modelo) devolve a
/// transcricao original. Ditado perdido e' pior do que ditado sem pontuacao.
/// </summary>
internal sealed class TextPostProcessor : IDisposable
{
    /// <summary>
    /// Instrucao padrao. Enfatiza NAO reescrever: o usuario ditou o que queria dizer, o modelo
    /// so' arruma a forma. Sem isto o modelo tende a "melhorar" o texto e mudar o sentido.
    /// </summary>
    public const string DefaultPrompt =
        "Você recebe a transcrição bruta de um ditado por voz. Devolva o MESMO texto com "
      + "pontuação, acentuação e capitalização corretas, removendo apenas hesitações e "
      + "muletas de fala (\"é...\", \"tipo\", \"né\", \"então assim\", repetições de gaguejo).\n"
      + "NÃO reescreva, não resuma, não traduza, não formate em markdown e não adicione nada. "
      + "Preserve o vocabulário, os termos técnicos e o tom de quem falou.\n"
      + "Responda APENAS com o texto corrigido, sem aspas, sem comentários e sem preâmbulo.";

    private readonly AnthropicClient _client;
    private readonly string _model;
    private readonly string _prompt;
    private readonly int _timeoutMs;

    /// <summary>
    /// Cria o pos-processador, ou devolve null se estiver desligado / sem chave de API.
    /// A chave sai da config ou da variavel de ambiente ANTHROPIC_API_KEY.
    /// </summary>
    public static TextPostProcessor? TryCreate(Config cfg)
    {
        if (!cfg.PostProcess) return null;

        var key = cfg.PostProcessApiKey.Length > 0
            ? cfg.PostProcessApiKey
            : Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") ?? "";
        if (key.Length == 0)
        {
            Logger.Warn("Pos-processamento ligado, mas sem chave de API (postProcessApiKey ou "
                      + "ANTHROPIC_API_KEY). Seguindo sem limpar o texto.");
            return null;
        }

        try { return new TextPostProcessor(cfg, key); }
        catch (Exception ex)
        {
            Logger.Error("Falha ao iniciar o pos-processamento; seguindo sem ele", ex);
            return null;
        }
    }

    private TextPostProcessor(Config cfg, string apiKey)
    {
        _client = new AnthropicClient { ApiKey = apiKey };
        _model = cfg.PostProcessModel;
        _prompt = cfg.PostProcessPrompt.Length > 0 ? cfg.PostProcessPrompt : DefaultPrompt;
        _timeoutMs = cfg.PostProcessTimeoutMs;
        Logger.Info($"Pos-processamento de texto ligado (modelo {_model}, timeout {_timeoutMs}ms).");
    }

    /// <summary>
    /// Devolve o texto limpo, ou o original se qualquer coisa der errado.
    /// Nunca lanca excecao: o chamador esta' no caminho critico do ditado.
    /// </summary>
    public async Task<string> CleanAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        try
        {
            using var cts = new CancellationTokenSource(_timeoutMs);
            var sw = System.Diagnostics.Stopwatch.StartNew();

            var response = await _client.Messages.Create(new MessageCreateParams
            {
                Model = _model,
                MaxTokens = 4096,
                // Ditado e' caminho quente: menor esforco = menos latencia. Mantemos o
                // raciocinio adaptativo ligado porque desliga-lo pode vazar tags <thinking>
                // no texto — que aqui iria direto pro editor do usuario.
                Thinking = new ThinkingConfigAdaptive(),
                OutputConfig = new OutputConfig { Effort = Effort.Low },
                System = new List<TextBlockParam> { new() { Text = _prompt } },
                Messages = [new() { Role = Role.User, Content = text }],
            }, cancellationToken: cts.Token);

            if (response.StopReason == "refusal")
            {
                Logger.Warn("Pos-processamento recusado pelo modelo; usando a transcricao original.");
                return text;
            }

            var cleaned = string.Concat(
                response.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text)).Trim();

            if (cleaned.Length == 0)
            {
                Logger.Warn("Pos-processamento devolveu vazio; usando a transcricao original.");
                return text;
            }

            sw.Stop();
            Logger.Info($"[pos] {sw.ElapsedMilliseconds}ms: \"{text}\" -> \"{cleaned}\"");
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

    public void Dispose() { /* AnthropicClient nao expoe recursos a liberar */ }
}
