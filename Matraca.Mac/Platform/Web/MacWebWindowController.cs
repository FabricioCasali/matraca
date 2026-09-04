using System.Text.Json;
using Matraca.Core;
using Matraca.Mac.Platform.Interop;

namespace Matraca.Mac.Platform.Web;

internal sealed class MacWebWindowController : IDisposable
{
    private readonly MacStatusItem _statusItem;
    private readonly MacApplication _application;
    private readonly MacWebBridge? _bridge;
    private readonly string _assetRoot;
    private MacWebViewHost? _host;
    private bool _disposed;

    public MacWebWindowController(
        MacApplication application,
        MacStatusItem statusItem,
        string assetRoot,
        MacWebBridge? bridge = null)
    {
        MainThread.VerifyAccess();
        _application = application ?? throw new ArgumentNullException(nameof(application));
        _statusItem = statusItem ?? throw new ArgumentNullException(nameof(statusItem));
        _bridge = bridge;
        ArgumentException.ThrowIfNullOrWhiteSpace(assetRoot);
        _assetRoot = assetRoot;
        _statusItem.OpenRequested += Open;
        if (_bridge != null)
        {
            _bridge.MessageProduced += PostToHost;
            _bridge.CloseWindowRequested += Close;
        }
    }

    public void Dispose()
    {
        MainThread.VerifyAccess();
        if (_disposed) return;
        _disposed = true;
        _statusItem.OpenRequested -= Open;
        if (_bridge != null)
        {
            _bridge.MessageProduced -= PostToHost;
            _bridge.CloseWindowRequested -= Close;
        }
        if (_host != null)
        {
            _host.MessageReceived -= OnMessageReceived;
            _host.WindowWillClose -= OnWindowWillClose;
            _host.Dispose();
            _host = null;
        }
    }

    internal void Open()
    {
        MainThread.VerifyAccess();
        if (_disposed) return;

        _host ??= CreateHost();
        try
        {
            if (!_host.IsVisible || _host.IsMiniaturized) _bridge?.PrepareToOpen();
            _application.ConfigureAsRegular();
            _host.ShowExplicitly();
        }
        catch
        {
            TryConfigureAsAccessory();
            throw;
        }
    }

    private MacWebViewHost CreateHost()
    {
        var host = new MacWebViewHost(_assetRoot);
        host.MessageReceived += OnMessageReceived;
        host.WindowWillClose += OnWindowWillClose;
        return host;
    }

    internal MacWebViewHost? Host => _host;

    private void OnWindowWillClose()
    {
        _bridge?.WindowClosed();
        TryConfigureAsAccessory();
    }

    private void Close() => MainThread.Run(() => _host?.Close());

    private void TryConfigureAsAccessory()
    {
        try { _application.ConfigureAsAccessory(); }
        catch (Exception exception)
        {
            Logger.Error("Falha ao restaurar o Matraca para o modo de bandeja", exception);
        }
    }

    private void OnMessageReceived(string json)
    {
        if (_bridge != null)
        {
            Observe(HandleMessageAsync(json));
            return;
        }

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

    private async Task HandleMessageAsync(string json)
    {
        string? response = await _bridge!.HandleAsync(json).ConfigureAwait(false);
        if (response != null) PostToHost(response);
    }

    private void PostToHost(string json)
        => MainThread.Run(() =>
        {
            if (!_disposed) _host?.PostJson(json);
        });

    private static void Observe(Task task)
        => _ = task.ContinueWith(
            failed => Logger.Error(
                "Falha ao processar mensagem da interface web",
                failed.Exception!.GetBaseException()),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
}
