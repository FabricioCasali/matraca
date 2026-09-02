namespace Matraca.Core;

public static class ModelDownloader
{
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

    public static string ModelsDir => AppPaths.Current().ModelsDirectory;

    public static string PathFor(ModelInfo model, AppPaths? paths = null)
        => Path.Combine((paths ?? AppPaths.Current()).ModelsDirectory, model.FileName);

    public static async Task<string> DownloadAsync(
        ModelInfo model,
        IProgress<(long done, long total)> progress,
        CancellationToken cancellationToken,
        AppPaths? paths = null,
        HttpClient? httpClient = null)
    {
        paths ??= AppPaths.Current();
        Directory.CreateDirectory(paths.ModelsDirectory);
        var finalPath = PathFor(model, paths);
        var partPath = finalPath + ".part";
        var ownsClient = httpClient == null;
        httpClient ??= new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

        try
        {
            using var response = await httpClient.GetAsync(
                model.Url,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            long total = response.Content.Headers.ContentLength ?? model.Bytes;
            long done = 0;

            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = new FileStream(
                partPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                1 << 20,
                useAsync: true))
            {
                var buffer = new byte[1 << 20];
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    done += read;
                    progress.Report((done, total));
                }
            }

            var actual = new FileInfo(partPath).Length;
            if (actual < model.Bytes / 2)
                throw new IOException(
                    $"Download incompleto: {actual} bytes, esperado ~{model.Bytes}. Tente de novo.");

            if (File.Exists(finalPath)) File.Delete(finalPath);
            File.Move(partPath, finalPath);
            Logger.Info($"Modelo baixado: {finalPath} ({actual / 1024 / 1024} MB)");
            return finalPath;
        }
        catch
        {
            try
            {
                if (File.Exists(partPath)) File.Delete(partPath);
            }
            catch
            {
                // Preserve the original download failure.
            }
            throw;
        }
        finally
        {
            if (ownsClient) httpClient.Dispose();
        }
    }

    public static List<string> Existing(AppPaths? paths = null)
    {
        var directory = (paths ?? AppPaths.Current()).ModelsDirectory;
        try
        {
            if (!Directory.Exists(directory)) return new List<string>();
            return Directory.GetFiles(directory, "*.bin").ToList();
        }
        catch
        {
            return new List<string>();
        }
    }

    public static string HumanSize(long bytes) => bytes >= 1L << 30
        ? $"{bytes / (double)(1L << 30):F1} GB"
        : $"{bytes / (double)(1L << 20):F0} MB";
}
