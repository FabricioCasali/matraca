using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text;
using System.Xml.Linq;

namespace WindowsUpdateProbe;

internal sealed class FixtureServer : IAsyncDisposable
{
    private readonly WebApplication app;
    private readonly ConcurrentDictionary<string, byte[]> files = new();
    private readonly ConcurrentDictionary<string, int> requests = new();
    public string BaseUrl => app.Urls.Single();
    public int Requests(string path) => requests.GetValueOrDefault(path);

    private FixtureServer(WebApplication app) => this.app = app;

    public static async Task<FixtureServer> StartAsync()
    {
        // Nao carregar appsettings, user-secrets ou variaveis da aplicacao hospedeira.
        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions
        {
            ContentRootPath = AppContext.BaseDirectory,
            EnvironmentName = "Probe"
        });
        builder.Logging.ClearProviders();
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        var server = new FixtureServer(builder.Build());
        server.app.Run(server.ServeAsync);
        await server.app.StartAsync();
        return server;
    }

    public void AddFeed(string name, byte[] signedPackage, TestSigningKey key,
        bool corruptPackage = false, bool corruptFeed = false, string version = "2.0.0", bool slow = false)
    {
        string path = $"/{name}/package.exe";
        var served = signedPackage.ToArray();
        if (corruptPackage) served[0] ^= 1;
        files[path] = served;
        XNamespace sparkle = "http://www.andymatuschak.org/xml-namespaces/sparkle";
        var xml = new XElement("rss", new XAttribute("version", "2.0"),
            new XAttribute(XNamespace.Xmlns + "sparkle", sparkle),
            new XElement("channel", new XElement("title", "MT038 isolated probe"),
                new XElement("item", new XElement("title", "Probe " + version),
                    new XElement("enclosure", new XAttribute("url", BaseUrl + path + (slow ? "?slow=1" : "")),
                        new XAttribute(sparkle + "version", version), new XAttribute(sparkle + "os", "windows"),
                        new XAttribute(sparkle + "signature", key.Sign(signedPackage)),
                        new XAttribute("length", signedPackage.Length), new XAttribute("type", "application/octet-stream")))));
        var bytes = Encoding.UTF8.GetBytes(xml.ToString());
        var signature = key.Sign(bytes);
        if (corruptFeed) bytes = Encoding.UTF8.GetBytes(xml.ToString() + " ");
        files[$"/{name}/appcast.xml"] = bytes;
        files[$"/{name}/appcast.xml.signature"] = Encoding.UTF8.GetBytes(signature);
    }

    public void SavePublicFixtures(string run, string publicKey)
    {
        var root = Path.Combine(run, "fixtures");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "test-public-key.pub"), publicKey);
        foreach (var (path, bytes) in files)
        {
            var target = Path.Combine(root, path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllBytes(target, bytes);
        }
    }

    private async Task ServeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value!;
        requests.AddOrUpdate(path, 1, (_, count) => count + 1);
        if (!files.TryGetValue(path, out var bytes)) { context.Response.StatusCode = 404; return; }
        context.Response.ContentLength = bytes.Length;
        context.Response.ContentType = path.EndsWith(".xml") ? "application/xml; charset=utf-8" : "application/octet-stream";
        if (HttpMethods.IsHead(context.Request.Method)) return;
        try
        {
            if (context.Request.Query.ContainsKey("slow"))
            {
                for (int offset = 0; offset < bytes.Length; offset += 8192)
                {
                    await context.Response.Body.WriteAsync(bytes.AsMemory(offset, Math.Min(8192, bytes.Length - offset)), context.RequestAborted);
                    await context.Response.Body.FlushAsync(context.RequestAborted);
                    await Task.Delay(50, context.RequestAborted);
                }
            }
            else await context.Response.Body.WriteAsync(bytes, context.RequestAborted);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
        catch (IOException) when (context.RequestAborted.IsCancellationRequested) { }
    }

    public async ValueTask DisposeAsync()
    {
        await app.StopAsync();
        await app.DisposeAsync();
    }
}
