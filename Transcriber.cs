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
    private readonly WhisperFactory _factory;
    private readonly string _language;

    public Transcriber(string modelPath, string language)
    {
        if (!File.Exists(modelPath))
            throw new FileNotFoundException($"Modelo Whisper nao encontrado: {modelPath}");

        _language = language;
        _factory = WhisperFactory.FromPath(modelPath);
        Logger.Info($"Modelo carregado: {modelPath}");
    }

    public async Task<string> TranscribeAsync(float[] samples)
    {
        if (samples.Length == 0) return "";

        await using var processor = _factory.CreateBuilder()
            .WithLanguage(_language)
            .Build();

        var sb = new StringBuilder();
        await foreach (var segment in processor.ProcessAsync(samples))
            sb.Append(segment.Text);

        return sb.ToString().Trim();
    }

    public void Dispose() => _factory.Dispose();
}
