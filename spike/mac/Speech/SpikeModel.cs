namespace Matraca.MacSpike.Speech;

/// <summary>
/// Garante o <c>ggml-base.bin</c> em disco. MESMA URL do
/// <c>ModelDownloader.Catalog</c> do lado Windows.
///
/// De proposito NAO reimplementa a logica de .part e retomada do ModelDownloader: sao vinte
/// linhas, o spike baixa uma vez, e o que interessa provar aqui e' a decisao de caminho —
/// ~/Library/Application Support/Matraca/models/ — que a Fase 1 vai formalizar no AppPaths.
/// </summary>
internal static class SpikeModel
{
    private const string Url =
        "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin";

    /// <summary>Tamanho esperado, copiado do catalogo do Windows.</summary>
    private const long ExpectedBytes = 147_951_465L;

    public static string ModelsDir => Path.Combine(SpikeLog.DataDir, "models");

    public static string EnsureBaseModel()
    {
        Directory.CreateDirectory(ModelsDir);
        var path = Path.Combine(ModelsDir, "ggml-base.bin");

        if (File.Exists(path) && new FileInfo(path).Length > ExpectedBytes / 2)
        {
            SpikeLog.Info($"modelo ja' em disco: {path} "
                        + $"({new FileInfo(path).Length / 1024 / 1024} MB)");
            return path;
        }

        SpikeLog.Info($"baixando {Url} ...");
        var t0 = SpikeLog.ElapsedMs;
        using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) })
        using (var input = http.GetStreamAsync(Url).GetAwaiter().GetResult())
        using (var output = File.Create(path))
            input.CopyTo(output);

        long got = new FileInfo(path).Length;
        // Um HTML de erro do Hugging Face tambem chega com HTTP 200; o tamanho denuncia.
        if (got < ExpectedBytes / 2)
        {
            File.Delete(path);
            throw new IOException($"download incompleto: {got} bytes, esperado ~{ExpectedBytes}.");
        }

        SpikeLog.Info($"modelo baixado: {path} ({got / 1024 / 1024} MB, "
                    + $"{(SpikeLog.ElapsedMs - t0) / 1000.0:F1} s)");
        return path;
    }
}
