using System.Text;
using Whisper.net;

namespace Matraca.Core;

public sealed class Transcriber : IDisposable
{
    private readonly WhisperFactory _factory;
    private readonly string _language;
    private readonly string _prompt;

    public Transcriber(string modelPath, string language, string[]? vocabulary = null)
    {
        if (!File.Exists(modelPath))
            throw new FileNotFoundException($"Modelo Whisper nao encontrado: {modelPath}");

        _language = language;
        _prompt = TranscriptionPromptBuilder.Build(vocabulary, Logger.Warn);
        _factory = WhisperFactory.FromPath(modelPath);
        Logger.Info($"Modelo carregado: {modelPath}"
                  + (_prompt.Length > 0 ? $" (vocabulario: {vocabulary!.Length} termos)" : ""));
    }

    public async Task<string> TranscribeAsync(
        float[] samples,
        CancellationToken cancellationToken = default)
    {
        if (samples.Length == 0) return "";

        var builder = _factory.CreateBuilder().WithLanguage(_language);
        if (_prompt.Length > 0) builder = builder.WithPrompt(_prompt);

        await using var processor = builder.Build();
        var text = new StringBuilder();
        await foreach (var segment in processor.ProcessAsync(samples, cancellationToken))
            text.Append(segment.Text);

        return text.ToString().Trim();
    }

    public void Dispose() => _factory.Dispose();
}
