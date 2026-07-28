namespace Matraca;

/// <summary>
/// Baixa modelos ggml do Whisper direto do repositorio oficial do whisper.cpp no Hugging Face.
/// Existe porque o Matraca nao embute modelo nenhum (sao centenas de MB) — sem isto o app
/// dependia de o usuario ja' ter um .bin baixado por outro programa.
/// </summary>
internal static class ModelDownloader
{
    /// <param name="Bytes">Tamanho esperado, usado p/ validar o download no fim.</param>
    public sealed record ModelInfo(string FileName, string Label, string Url, long Bytes);

    private const string BaseUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/";

    public static readonly ModelInfo[] Catalog =
    {
        new("ggml-large-v3-turbo.bin", "large-v3-turbo — melhor qualidade (1,5 GB, precisa de GPU)",
            BaseUrl + "ggml-large-v3-turbo.bin", 1_624_555_275L),
        new("ggml-small.bin", "small — bom equilíbrio (465 MB)",
            BaseUrl + "ggml-small.bin", 487_601_967L),
        new("ggml-base.bin", "base — leve e rápido, menos preciso (141 MB)",
            BaseUrl + "ggml-base.bin", 147_951_465L),
    };

    /// <summary>Onde os modelos baixados ficam: %LOCALAPPDATA%\Matraca\models.</summary>
    public static string ModelsDir => Path.Combine(Logger.DataDir, "models");

    public static string PathFor(ModelInfo m) => Path.Combine(ModelsDir, m.FileName);

    /// <summary>
    /// Baixa o modelo e devolve o caminho final. Escreve num .part e so' renomeia no fim —
    /// assim um download interrompido nunca vira um .bin corrompido que o app tentaria carregar.
    /// </summary>
    public static async Task<string> DownloadAsync(
        ModelInfo model, IProgress<(long done, long total)> progress, CancellationToken ct)
    {
        Directory.CreateDirectory(ModelsDir);
        var finalPath = PathFor(model);
        var partPath = finalPath + ".part";

        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        using var resp = await http.GetAsync(model.Url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        long total = resp.Content.Headers.ContentLength ?? model.Bytes;
        long done = 0;

        await using (var input = await resp.Content.ReadAsStreamAsync(ct))
        await using (var output = new FileStream(partPath, FileMode.Create, FileAccess.Write,
                                                 FileShare.None, 1 << 20, useAsync: true))
        {
            var buffer = new byte[1 << 20];
            int read;
            while ((read = await input.ReadAsync(buffer, ct)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), ct);
                done += read;
                progress.Report((done, total));
            }
        }

        // Um HTML de erro do Hugging Face tambem chega com HTTP 200; o tamanho denuncia.
        var actual = new FileInfo(partPath).Length;
        if (actual < model.Bytes / 2)
        {
            try { File.Delete(partPath); } catch { }
            throw new IOException(
                $"Download incompleto: {actual} bytes, esperado ~{model.Bytes}. Tente de novo.");
        }

        if (File.Exists(finalPath)) File.Delete(finalPath);
        File.Move(partPath, finalPath);
        Logger.Info($"Modelo baixado: {finalPath} ({actual / 1024 / 1024} MB)");
        return finalPath;
    }

    /// <summary>Modelos ja' baixados na pasta do app (p/ oferecer sem baixar de novo).</summary>
    public static List<string> Existing()
    {
        try
        {
            if (!Directory.Exists(ModelsDir)) return new List<string>();
            return Directory.GetFiles(ModelsDir, "*.bin").ToList();
        }
        catch { return new List<string>(); }
    }

    public static string HumanSize(long bytes) => bytes >= 1L << 30
        ? $"{bytes / (double)(1L << 30):F1} GB"
        : $"{bytes / (double)(1L << 20):F0} MB";
}
