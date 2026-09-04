using System.Text.Json;
using Matraca.Core;
using Matraca.Mac.Platform.Interop;

namespace Matraca.Mac.Platform.Web;

internal sealed class MacHudWindowController : IDisposable
{
    private readonly MacTrayApp _app;
    private readonly MacWebViewHost _host;
    private bool _ready;
    private bool _dictationActive;
    private bool _deliveryJustCompleted;
    private int _hideGeneration;
    private bool _disposed;

    public MacHudWindowController(MacTrayApp app, string authorizedAssetRoot)
    {
        MainThread.VerifyAccess();
        _app = app ?? throw new ArgumentNullException(nameof(app));
        _host = new MacWebViewHost(
            authorizedAssetRoot,
            "Matraca HUD",
            width: 430,
            height: 92,
            entryPath: "hud.html",
            nonActivatingOverlay: true);
        _host.MessageReceived += OnMessageReceived;
        _app.StateChanged += OnStateChanged;
        _app.DeliveryStarted += OnDeliveryStarted;
        _app.DeliveryCompleted += OnDeliveryCompleted;
    }

    private void OnMessageReceived(string message)
    {
        using JsonDocument document = JsonDocument.Parse(message);
        if (document.RootElement.TryGetProperty("type", out JsonElement type)
            && type.GetString() == "ui.hudReady")
        {
            _ready = true;
            ApplyState(_app.CurrentState, _app.CurrentStateText);
        }
    }

    private void OnStateChanged(ShellState state, string text)
        => MainThread.Post(() => ApplyState(state, text));

    private void ApplyState(ShellState state, string text)
    {
        if (_disposed || !_ready) return;
        if (state == ShellState.Idle)
        {
            _dictationActive = false;
            if (_deliveryJustCompleted)
            {
                _deliveryJustCompleted = false;
            }
            else
            {
                Interlocked.Increment(ref _hideGeneration);
                _host.Hide();
            }
            return;
        }

        Interlocked.Increment(ref _hideGeneration);
        if (state == ShellState.Recording)
        {
            _dictationActive = true;
        }
        bool writing = state == ShellState.Busy
            && text.Contains("escrevendo", StringComparison.OrdinalIgnoreCase);
        string name = writing ? "writing" : state switch
        {
            ShellState.Recording => "listening",
            ShellState.Busy => "thinking",
            ShellState.Error => "error",
            _ => "ready",
        };
        string title = writing ? "Escrevendo" : state switch
        {
            ShellState.Recording => "Ouvindo voce",
            ShellState.Busy => "Pensando",
            ShellState.Error => "Atencao",
            _ => "Matraca",
        };
        Publish(
            name,
            title,
            state == ShellState.Recording ? _app.CurrentConfig.Mode : text);
    }

    private void OnDeliveryCompleted(string text, TextDeliveryResult result, bool streaming)
        => MainThread.Post(() => ApplyDeliveryResult(text, result, streaming));

    private void OnDeliveryStarted(bool streaming)
        => MainThread.Post(() =>
        {
            if (!_disposed && _ready)
                Publish("writing", "Escrevendo", streaming ? "live" : _app.CurrentConfig.Mode);
        });

    private void ApplyDeliveryResult(string text, TextDeliveryResult result, bool streaming)
    {
        if (_disposed || !_ready) return;
        bool delivered = result == TextDeliveryResult.Delivered;
        _deliveryJustCompleted = !streaming;
        Publish(
            delivered ? "done" : "error",
            delivered ? "Texto entregue" : "Entrega falhou",
            text);
        if (streaming && _dictationActive)
            ReturnToListeningLater();
        else
            HideLater();
    }

    private void Publish(string state, string title, string detail)
    {
        if (_app.TryGetActiveTargetBounds(out CGRect target))
            _host.PositionOverlayForTarget(target);
        _host.PostJson(JsonSerializer.Serialize(new
        {
            version = 1,
            type = "hud.state",
            payload = new
            {
                state,
                title,
                detail,
            },
        }));
        _host.ShowExplicitly();
    }

    private void HideLater()
    {
        int generation = Interlocked.Increment(ref _hideGeneration);
        _ = Task.Delay(1600).ContinueWith(
            _ => MainThread.Post(() =>
            {
                if (!_disposed && generation == Volatile.Read(ref _hideGeneration))
                    _host.Hide();
            }),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void ReturnToListeningLater()
    {
        int generation = Interlocked.Increment(ref _hideGeneration);
        _ = Task.Delay(900).ContinueWith(
            _ => MainThread.Post(() =>
            {
                if (!_disposed
                    && generation == Volatile.Read(ref _hideGeneration)
                    && _dictationActive)
                    Publish("listening", "Ouvindo voce", _app.CurrentConfig.Mode);
            }),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    public void Dispose()
    {
        MainThread.VerifyAccess();
        if (_disposed) return;
        _disposed = true;
        _app.StateChanged -= OnStateChanged;
        _app.DeliveryStarted -= OnDeliveryStarted;
        _app.DeliveryCompleted -= OnDeliveryCompleted;
        _host.MessageReceived -= OnMessageReceived;
        _host.Dispose();
    }
}
