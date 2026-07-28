using System.Text;
using Whisper.net;

namespace Matraca;

/// <summary>
/// Wrapper do Whisper.net. Carrega o modelo ggml (.bin) uma unica vez.
/// A selecao de runtime (Vulkan/GPU -> CPU) e automatica conforme os pacotes
/// Whisper.net.Runtime.* referenciados.
/// </summary>
internal sealed class Transcriber : IDisposable
{
    /// <summary>
    /// O prompt inicial disputa espaco com o audio na janela de contexto do Whisper, entao
    /// vocabulario demais atrapalha em vez de ajudar. Corta no que cabe com folga.
    /// </summary>
    private const int MaxPromptChars = 800;

    private readonly WhisperFactory _factory;
    private readonly string _language;
    private readonly string _prompt;

    public Transcriber(string modelPath, string language, string[]? vocabulary = null)
    {
        if (!File.Exists(modelPath))
            throw new FileNotFoundException($"Modelo Whisper nao encontrado: {modelPath}");

        _language = language;
        _prompt = BuildPrompt(vocabulary);
        _factory = WhisperFactory.FromPath(modelPath);
        Logger.Info($"Modelo carregado: {modelPath}"
                  + (_prompt.Length > 0 ? $" (vocabulario: {vocabulary!.Length} termos)" : ""));
    }

    /// <summary>
    /// Monta o prompt inicial a partir do vocabulario. O Whisper usa o prompt como se fosse a
    /// transcricao do trecho anterior, entao uma lista simples de termos ja o enviesa a
    /// reconhece-los — sem inventar frase nenhuma.
    /// </summary>
    private static string BuildPrompt(string[]? vocabulary)
    {
        if (vocabulary == null || vocabulary.Length == 0) return "";

        var sb = new StringBuilder();
        foreach (var term in vocabulary)
        {
            if (sb.Length + term.Length + 2 > MaxPromptChars)
            {
                Logger.Warn($"Vocabulario truncado em {MaxPromptChars} caracteres; " +
                            "termos do fim da lista foram ignorados.");
                break;
            }
            if (sb.Length > 0) sb.Append(", ");
            sb.Append(term);
        }
        return sb.ToString();
    }

    public async Task<string> TranscribeAsync(float[] samples)
    {
        if (samples.Length == 0) return "";

        var builder = _factory.CreateBuilder().WithLanguage(_language);
        if (_prompt.Length > 0) builder = builder.WithPrompt(_prompt);

        await using var processor = builder.Build();

        var sb = new StringBuilder();
        await foreach (var segment in processor.ProcessAsync(samples))
            sb.Append(segment.Text);

        return sb.ToString().Trim();
    }

    public void Dispose() => _factory.Dispose();
}
