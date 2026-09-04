using System.Net;
using Xunit;

namespace Matraca.Core.Tests;

public sealed class ModelDownloaderTests
{
    [Fact]
    public async Task DownloadPromotesCompletePartFileAndReportsProgress()
    {
        var home = NewTemporaryDirectory();
        try
        {
            var paths = AppPaths.ForMac(home);
            var bytes = new byte[] { 1, 2, 3, 4 };
            var model = new ModelInfo("test.bin", "test", "https://offline.test/model", bytes.Length);
            using var client = ClientReturning(HttpStatusCode.OK, bytes);
            (long done, long total) reported = default;
            var progress = new InlineProgress<(long done, long total)>(value => reported = value);

            var result = await ModelDownloader.DownloadAsync(
                model,
                progress,
                CancellationToken.None,
                paths,
                client);

            Assert.Equal(ModelDownloader.PathFor(model, paths), result);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(result));
            Assert.False(File.Exists(result + ".part"));
            Assert.Equal(bytes.Length, reported.done);
            Assert.Equal(bytes.Length, reported.total);
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }

    [Fact]
    public async Task IncompleteDownloadDeletesPartFile()
    {
        var home = NewTemporaryDirectory();
        try
        {
            var paths = AppPaths.ForMac(home);
            var model = new ModelInfo("short.bin", "test", "https://offline.test/model", 100);
            using var client = ClientReturning(HttpStatusCode.OK, [1, 2, 3]);

            await Assert.ThrowsAsync<IOException>(() => ModelDownloader.DownloadAsync(
                model,
                new Progress<(long, long)>(),
                CancellationToken.None,
                paths,
                client));

            Assert.False(File.Exists(ModelDownloader.PathFor(model, paths)));
            Assert.False(File.Exists(ModelDownloader.PathFor(model, paths) + ".part"));
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }

    [Fact]
    public async Task WrongSizedDownloadDeletesPartFileEvenWhenMoreThanHalfArrived()
    {
        var home = NewTemporaryDirectory();
        try
        {
            var paths = AppPaths.ForMac(home);
            var model = new ModelInfo("truncated.bin", "test", "https://offline.test/model", 4);
            using var client = ClientReturning(HttpStatusCode.OK, [1, 2, 3]);

            await Assert.ThrowsAsync<IOException>(() => ModelDownloader.DownloadAsync(
                model,
                new Progress<(long, long)>(),
                CancellationToken.None,
                paths,
                client));

            Assert.False(File.Exists(ModelDownloader.PathFor(model, paths) + ".part"));
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }

    [Fact]
    public async Task CancellationDeletesStalePartFileWithoutNetwork()
    {
        var home = NewTemporaryDirectory();
        try
        {
            var paths = AppPaths.ForMac(home);
            var model = new ModelInfo("cancel.bin", "test", "https://offline.test/model", 4);
            var partPath = ModelDownloader.PathFor(model, paths) + ".part";
            Directory.CreateDirectory(paths.ModelsDirectory);
            await File.WriteAllTextAsync(partPath, "stale");
            using var client = ClientReturning(HttpStatusCode.OK, [1, 2, 3, 4]);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ModelDownloader.DownloadAsync(
                model,
                new Progress<(long, long)>(),
                cancellation.Token,
                paths,
                client));

            Assert.False(File.Exists(partPath));
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }

    private static HttpClient ClientReturning(HttpStatusCode status, byte[] content)
        => new(new StubHttpMessageHandler((_, _) => new HttpResponseMessage(status)
        {
            Content = new ByteArrayContent(content),
        }));

    private static string NewTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "matraca-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
