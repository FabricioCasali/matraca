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
    private bool _deliveryError;
    private int _hideGeneration;
    private bool _disposed;
    private ShellState _currentState;
    private string _currentStateText = "";
    private string _pendingState = "ready";
    private string _pendingTitle = "Matraca";
    private string _pendingDetail = "";

    public MacHudWindowController(MacTrayApp app, string authorizedAssetRoot)
    {
        MainThread.VerifyAccess();
        _app = app ?? throw new ArgumentNullException(nameof(app));
        _host = new MacWebViewHost(authorizedAssetRoot, "Matraca HUD",
            width: 430, height: 92, entryPath: "hud.html", nonActivatingOverlay: true,
            effectiveLanguage: () => _app.CurrentConfig.EffectiveUiLanguage);
        _host.MessageReceived += OnMessageReceived;
        _app.StateChanged += OnStateChanged;
        _app.DeliveryStarted += OnDeliveryStarted;
        _app.DeliveryCompleted += OnDeliveryCompleted;
        _app.ConfigChanged += OnConfigChanged;
        ApplyState(_app.CurrentState, _app.CurrentStateText);
    }

    private void OnMessageReceived(string message)
    {
        if (_disposed) return;
        try
        {
            using JsonDocument document = JsonDocument.Parse(message);
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("version", out JsonElement version)
                || version.ValueKind != JsonValueKind.Number
                || !version.TryGetInt32(out int number) || number != 1
                || !root.TryGetProperty("type", out JsonElement type)
                || type.ValueKind != JsonValueKind.String
                || type.GetString() != "ui.hudReady") return;
        }
        catch (JsonException) { return; }
        _ready = true;
        PublishAppearance();
        if (_pendingState != "ready") Publish(_pendingState, _pendingTitle, _pendingDetail, invalidateTimers: false);
    }

    private void OnConfigChanged(Matraca.Core.Config config)
        => MainThread.Post(() =>
        {
            if (_disposed) return;
            if (_ready) PublishAppearance();
            ApplyState(_currentState, _currentStateText);
        });

    private void PublishAppearance()
    {
        RawConfig raw = _app.LoadRawConfig();
        _host.PostJson(JsonSerializer.Serialize(new
        {
            version = 1,
            type = "hud.appearance",
            payload = new
            {
                themeMode = raw.themeMode ?? "system",
                palette = raw.palette ?? "olive",
                uiLanguage = _app.CurrentConfig.UiLanguage,
                effectiveUiLanguage = _app.CurrentConfig.EffectiveUiLanguage,
            },
        }));
    }

    private void OnStateChanged(ShellState state, string text)
        => MainThread.Post(() =>
        {
            _currentState = state;
            _currentStateText = text;
            ApplyState(state, text);
        });

    private void ApplyState(ShellState state, string text)
    {
        if (_disposed) return;
        if (state == ShellState.Idle)
        {
            _dictationActive = false;
            // Ready also follows failed delivery; it is not an acknowledgement of the error.
            if (_deliveryError) return;
            if (_deliveryJustCompleted) _deliveryJustCompleted = false;
            else Hide();
            return;
        }
        if (state == ShellState.Busy) _dictationActive = false;
        if (_deliveryError && !(state == ShellState.Recording && !_dictationActive)) return;
        _deliveryError = false;
        if (state == ShellState.Recording) _dictationActive = true;
        MacUiText ui = new(_app.CurrentConfig.EffectiveUiLanguage);
        string name = state switch
        {
            ShellState.Recording => "listening",
            ShellState.Busy => "thinking",
            ShellState.Writing => "writing",
            ShellState.Error => "error",
            _ => "ready",
        };
        string title = state switch
        {
            ShellState.Recording => ui.HudListening,
            ShellState.Busy => ui.HudThinking,
            ShellState.Writing => ui.HudWriting,
            ShellState.Error => ui.HudAttention,
            _ => ui.HudReady,
        };
        string detail = state switch
        {
            ShellState.Recording => _app.CurrentConfig.Mode,
            ShellState.Busy => ui.HudThinking,
            ShellState.Writing => ui.HudWriting,
            ShellState.Error => ui.HudAttention,
            _ => text,
        };
        Publish(name, title, detail);
    }

    private void OnDeliveryCompleted(string text, TextDeliveryResult result, bool streaming)
        => MainThread.Post(() => ApplyDeliveryResult(text, result, streaming));

    private void OnDeliveryStarted(bool streaming)
        => MainThread.Post(() =>
        {
            if (_disposed) return;
            _deliveryJustCompleted = false;
            Interlocked.Increment(ref _hideGeneration);
            if (_deliveryError) return;
            MacUiText ui = new(_app.CurrentConfig.EffectiveUiLanguage);
            Publish("writing", streaming && _dictationActive ? ui.HudListeningInserting : ui.HudInserting,
                _app.CurrentConfig.Mode);
        });

    private void ApplyDeliveryResult(string text, TextDeliveryResult result, bool streaming)
    {
        if (_disposed) return;
        bool delivered = result == TextDeliveryResult.Delivered;
        _deliveryError = !delivered;
        _deliveryJustCompleted = !streaming || !_dictationActive;
        MacUiText ui = new(_app.CurrentConfig.EffectiveUiLanguage);
        Publish(delivered ? "done" : "error",
            delivered
                ? (streaming && _dictationActive ? ui.HudSegmentDelivered : ui.HudTextDelivered)
                : ui.HudDeliveryFailed,
            text);
        if (!delivered) return;
        if (streaming && _dictationActive) ReturnToListeningLater();
        else HideLater();
    }

    private void Publish(string state, string title, string detail, bool invalidateTimers = true)
    {
        if (invalidateTimers) Interlocked.Increment(ref _hideGeneration);
        _pendingState = state;
        _pendingTitle = title;
        _pendingDetail = detail;
        if (!_ready) return;
        if (_app.TryGetHudTargetBounds(out CGRect target)) _host.PositionOverlayForTarget(target);
        _host.PostJson(JsonSerializer.Serialize(new
        {
            version = 1,
            type = "hud.state",
            payload = new { state, title, detail, generation = _hideGeneration },
        }));
        _host.ShowExplicitly();
    }

    private void Hide()
    {
        int generation = Interlocked.Increment(ref _hideGeneration);
        _pendingState = "ready";
        if (!_ready) return;
        _host.PostJson(JsonSerializer.Serialize(new
        {
            version = 1,
            type = "hud.state",
            payload = new { state = "ready", title = "Matraca", detail = "", generation },
        }));
        _ = Task.Delay(140).ContinueWith(
            _ => MainThread.Post(() =>
            {
                if (!_disposed && generation == Volatile.Read(ref _hideGeneration)) _host.Hide();
            }), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private void HideLater()
    {
        int generation = Volatile.Read(ref _hideGeneration);
        _ = Task.Delay(1600).ContinueWith(
            _ => MainThread.Post(() =>
            {
                if (!_disposed && generation == Volatile.Read(ref _hideGeneration)) Hide();
            }), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private void ReturnToListeningLater()
    {
        int generation = Volatile.Read(ref _hideGeneration);
        _ = Task.Delay(900).ContinueWith(
            _ => MainThread.Post(() =>
            {
                if (!_disposed && generation == Volatile.Read(ref _hideGeneration) && _dictationActive)
                    Publish("listening", new MacUiText(_app.CurrentConfig.EffectiveUiLanguage).HudListening,
                        _app.CurrentConfig.Mode);
            }), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    public void Dispose()
    {
        MainThread.VerifyAccess();
        if (_disposed) return;
        _disposed = true;
        Interlocked.Increment(ref _hideGeneration);
        _app.StateChanged -= OnStateChanged;
        _app.DeliveryStarted -= OnDeliveryStarted;
        _app.DeliveryCompleted -= OnDeliveryCompleted;
        _app.ConfigChanged -= OnConfigChanged;
        _host.MessageReceived -= OnMessageReceived;
        _host.Dispose();
    }
}
