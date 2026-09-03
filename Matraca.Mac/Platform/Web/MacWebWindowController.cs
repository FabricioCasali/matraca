using System.Text.Json;
using Matraca.Core;
using Matraca.Mac.Platform.Interop;

namespace Matraca.Mac.Platform.Web;

internal sealed class MacWebWindowController : IDisposable
{
    private readonly MacStatusItem _statusItem;
    private readonly string _assetRoot;
    private MacWebViewHost? _host;
    private bool _disposed;

    public MacWebWindowController(MacStatusItem statusItem, string assetRoot)
    {
        MainThread.VerifyAccess();
        _statusItem = statusItem ?? throw new ArgumentNullException(nameof(statusItem));
        ArgumentException.ThrowIfNullOrWhiteSpace(assetRoot);
        _assetRoot = assetRoot;
        _statusItem.OpenRequested += Open;
    }

    public void Dispose()
    {
        MainThread.VerifyAccess();
        if (_disposed) return;
        _disposed = true;
        _statusItem.OpenRequested -= Open;
        if (_host != null)
        {
            _host.MessageReceived -= OnMessageReceived;
            _host.Dispose();
            _host = null;
        }
    }

    private void Open()
    {
        MainThread.VerifyAccess();
        if (_disposed) return;

        _host ??= CreateHost();
        _host.ShowExplicitly();
    }

    private MacWebViewHost CreateHost()
    {
        var host = new MacWebViewHost(_assetRoot);
        host.MessageReceived += OnMessageReceived;
        return host;
    }

    private void OnMessageReceived(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("version", out JsonElement version)
                || version.ValueKind != JsonValueKind.Number
                || version.GetInt32() != 1
                || !root.TryGetProperty("type", out JsonElement type)
                || type.GetString() != "request"
                || !root.TryGetProperty("id", out JsonElement id)
                || id.ValueKind != JsonValueKind.String)
                return;

            _host?.PostJson(JsonSerializer.Serialize(new
            {
                version = 1,
                id = id.GetString(),
                type = "response",
                ok = false,
                error = new
                {
                    code = "not_implemented",
                    message = "This native operation is not connected yet.",
                },
            }));
        }
        catch (Exception exception)
        {
            Logger.Error("Falha ao responder mensagem da interface web", exception);
        }
    }
}
