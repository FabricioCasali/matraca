using System.Text;
using Whisper.net;
using Whisper.net.Logger;

namespace Matraca.MacSpike.Speech;

/// <summary>
/// Whisper.net no macOS arm64, com o log nativo do whisper.cpp capturado.
///
/// A pergunta que esta perna existe para responder NAO e' "roda?" — o pacote
/// Whisper.net.Runtime 1.9.1 traz libggml-metal-whisper.dylib em build/macos-arm64, e um TFM
/// sem traco a copia para runtimes/macos-arm64. A pergunta e' "o ggml ELEGE o backend Metal
/// em runtime?", e so' a linha que o whisper.cpp imprime ao carregar o modelo responde
/// (ggml_metal_init: picking default device / whisper_backend_init_gpu: using Metal backend).
///
/// Por isso o LogProvider e' ligado ANTES de qualquer coisa: rodando pelo terminal essas
/// linhas sairiam em stderr de graca, mas capturando-as no nosso log elas ficam no arquivo,
/// junto dos timestamps, e viram evidencia citavel no README.
/// </summary>
internal sealed class SpikeTranscriber : IDisposable
{
    private readonly WhisperFactory _factory;
    private readonly string _language;

    /// <summary>Toda linha que o whisper.cpp cuspiu, na ordem.</summary>
    public static readonly List<string> NativeLog = new();

    private static bool _loggerHooked;

    /// <summary>Liga a captura do log nativo. Chamar UMA vez, antes de carregar o modelo.</summary>
    public static void HookNativeLog()
    {
        if (_loggerHooked) return;
        _loggerHooked = true;

        LogProvider.AddLogger((level, message) =>
        {
            var line = (message ?? string.Empty).TrimEnd('\n', '\r');
            if (line.Length == 0) return;
            lock (NativeLog) NativeLog.Add(line);
            SpikeLog.Info($"[whisper.cpp/{level}] {line}");
        });
    }

    public SpikeTranscriber(string modelPath, string language = "pt")
    {
        _language = language;
        var t0 = SpikeLog.ElapsedMs;
        _factory = WhisperFactory.FromPath(modelPath);
        SpikeLog.Info($"modelo carregado em {SpikeLog.ElapsedMs - t0} ms: {modelPath}");
    }

    /// <summary>Transcreve e devolve o texto; <paramref name="wallMs"/> e' o tempo de parede.</summary>
    public string Transcribe(float[] samples, out long wallMs)
    {
        var t0 = SpikeLog.ElapsedMs;
        var sb = new StringBuilder();

        using (var processor = _factory.CreateBuilder().WithLanguage(_language).Build())
            foreach (var segment in processor.ProcessAsync(samples).ToBlockingEnumerable())
                sb.Append(segment.Text);

        wallMs = SpikeLog.ElapsedMs - t0;
        return sb.ToString().Trim();
    }

    /// <summary>
    /// O veredito do risco 3, tirado do log nativo: o ggml elegeu Metal ou caiu para CPU?
    /// </summary>
    public static string BackendVerdict()
    {
        lock (NativeLog)
        {
            bool metal = NativeLog.Any(l => l.Contains("metal", StringComparison.OrdinalIgnoreCase));
            bool blas = NativeLog.Any(l => l.Contains("blas", StringComparison.OrdinalIgnoreCase));
            if (metal) return "METAL";
            if (blas) return "CPU+BLAS";
            return "indefinido (nenhuma linha do whisper.cpp mencionou backend)";
        }
    }

    public void Dispose() => _factory.Dispose();
}
