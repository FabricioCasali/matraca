using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Matraca.Core;
using Matraca.Mac.Config;
using Matraca.Mac.Platform.Audio;
using Matraca.Mac.Platform.Interop;

namespace Matraca.Mac.Platform.Web;

internal sealed class MacWebBridge : IDisposable
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly MacTrayApp? _app;
    private readonly MacMicrophoneMonitor _microphone = new();
    private readonly SemaphoreSlim _microphoneGate = new(1, 1);
    private readonly object _hotkeyCaptureGate = new();
    private readonly object _modelDownloadGate = new();
    private readonly float[] _smoothedBands = new float[SpectrumAnalyzer.BandCount];
    private string _monitoredDevice = "";
    private float _peak;
    private int _microphoneGeneration;
    private CancellationTokenSource? _hotkeyCaptureCancellation;
    private CancellationTokenSource? _modelDownloadCancellation;
    private Task<object>? _modelDownloadTask;
    private int _stopping;
    private int _disposed;

    public MacWebBridge(MacTrayApp? app)
    {
        _app = app;
        _microphone.Frame += OnMicrophoneFrame;
        if (_app == null) return;
        _app.StateChanged += OnStateChanged;
        _app.ConfigChanged += OnConfigChanged;
    }

    public event Action<string>? MessageProduced;
    public event Action? CloseWindowRequested;
    public bool CanOpenWindow => _app?.CanOpenWebWindow ?? true;

    public void RejectOpenWhileBusy()
        => _app?.ShowWebError("Encerre o ditado antes de abrir o painel.");

    public void PrepareToOpen()
    {
        _app?.CaptureWebTarget();
        Emit("window.opened", new { });
    }

    public void WindowClosed()
    {
        CancelHotkeyCapture();
        _app?.ReleaseWebTarget();
        Observe(StopMicrophoneAsync());
    }

    public async Task<string?> HandleAsync(string json)
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
                || !root.TryGetProperty("id", out JsonElement idElement)
                || idElement.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("method", out JsonElement methodElement)
                || methodElement.ValueKind != JsonValueKind.String)
                return null;

            string id = idElement.GetString()!;
            string method = methodElement.GetString()!;
            JsonElement parameters = root.TryGetProperty("params", out JsonElement value)
                ? value
                : default;
            object result = method switch
            {
                "app.get" => BuildSnapshot(),
                "config.get" => BuildConfig(),
                "config.set" => await SetConfigAsync(parameters).ConfigureAwait(false),
                "history.list" => BuildHistory(),
                "history.delete" => DeleteHistory(parameters),
                "history.copy" => CopyHistory(parameters),
                "history.repaste" => await RepasteHistoryAsync(parameters).ConfigureAwait(false),
                "hotkey.capture.start" => await CaptureHotkeyAsync().ConfigureAwait(false),
                "hotkey.capture.cancel" => CancelHotkeyCapture(),
                "model.download.start" => await DownloadModelAsync(parameters).ConfigureAwait(false),
                "model.download.cancel" => CancelModelDownload(),
                "mic.monitor.start" => await StartMicrophoneAsync(parameters).ConfigureAwait(false),
                "mic.monitor.stop" => await StopMicrophoneAsync().ConfigureAwait(false),
                "permissions.get" => BuildPermissions(),
                "permissions.open-settings" => OpenPermissionSettings(parameters),
                _ => throw new NotSupportedException(method),
            };
            return Response(id, result);
        }
        catch (NotSupportedException exception)
        {
            return ErrorResponse(GetRequestId(json), "not_implemented", exception.Message);
        }
        catch (JsonException exception)
        {
            return ErrorResponse(GetRequestId(json), "invalid_request", exception.Message);
        }
        catch (UnauthorizedAccessException exception)
        {
            return ErrorResponse(GetRequestId(json), "permission_denied", exception.Message);
        }
        catch (OperationCanceledException exception)
        {
            return ErrorResponse(GetRequestId(json), "canceled", exception.Message);
        }
        catch (Exception exception)
        {
            Logger.Error("Falha ao executar comando da interface web", exception);
            return ErrorResponse(GetRequestId(json), "native_error", exception.Message);
        }
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _stopping, 1) != 0) return;
        Task<object>? modelDownload;
        lock (_modelDownloadGate)
        {
            _modelDownloadCancellation?.Cancel();
            modelDownload = _modelDownloadTask;
        }
        if (modelDownload != null)
        {
            try { await modelDownload.WaitAsync(cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (Exception exception) { Logger.Error("Download de modelo falhou no encerramento", exception); }
        }
        Interlocked.Increment(ref _microphoneGeneration);
        await _microphoneGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await _microphone.StopAsync(cancellationToken).ConfigureAwait(false); }
        finally { _microphoneGate.Release(); }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        ShutdownAsync().GetAwaiter().GetResult();
        if (_app != null)
        {
            _app.StateChanged -= OnStateChanged;
            _app.ConfigChanged -= OnConfigChanged;
            _app.ReleaseWebTarget();
        }
        CancelHotkeyCapture();
        _microphone.Frame -= OnMicrophoneFrame;
        _microphone.Dispose();
        MessageProduced = null;
        CloseWindowRequested = null;
    }

    private object BuildSnapshot() => new
    {
        config = BuildConfig(),
        history = BuildHistory(),
        state = BuildState(),
        permissions = BuildPermissions(),
        devices = _app?.ListAudioDevices() ?? _microphone.ListDevices(),
        models = BuildModels(),
    };

    private object BuildConfig()
    {
        RawConfig raw = _app?.LoadRawConfig() ?? MacConfig.LoadRaw();
        return new
        {
            config = BuildPublicConfig(raw),
            runtime = _app == null ? null : new
            {
                gpu = _app.RuntimeGpu,
                desiredGpu = _app.CurrentConfig.Gpu,
                restartRequired = _app.CurrentConfig.Gpu != _app.RuntimeGpu,
            },
        };
    }

    private async Task<object> SetConfigAsync(JsonElement parameters)
    {
        if (_app == null)
            throw new InvalidOperationException("O runtime de ditado nao esta disponivel.");
        if (parameters.ValueKind != JsonValueKind.Object
            || !parameters.TryGetProperty("patch", out JsonElement patchElement)
            || patchElement.ValueKind != JsonValueKind.Object)
            throw new JsonException("config.set exige params.patch.");

        (RawConfig raw, bool restartRequired) = await _app
            .ApplyAndSaveConfigPatchAsync(patchElement.GetRawText())
            .ConfigureAwait(false);
        return new { config = BuildPublicConfig(raw), restartRequired };
    }

    private object BuildHistory() => new
    {
        entries = (_app?.HistorySnapshot() ?? [])
            .Select(entry => new
            {
                id = HistoryId(entry),
                at = entry.At,
                text = entry.Text,
                characterCount = entry.Text.Length,
            })
            .ToArray(),
    };

    private object DeleteHistory(JsonElement parameters)
    {
        DictationHistoryEntry entry = FindHistoryEntry(parameters);
        if (_app?.RemoveHistory(entry.At, entry.Text) != true)
            throw new IOException("Nao foi possivel persistir a exclusao do historico.");
        return new { deleted = true };
    }

    private object CopyHistory(JsonElement parameters)
    {
        DictationHistoryEntry entry = FindHistoryEntry(parameters);
        if (_app?.CopyText(entry.Text) != true)
            throw new IOException("O clipboard mudou ou recusou a copia.");
        return new { copied = true };
    }

    private async Task<object> RepasteHistoryAsync(JsonElement parameters)
    {
        if (_app == null)
            throw new InvalidOperationException("O runtime de ditado nao esta disponivel.");
        DictationHistoryEntry entry = FindHistoryEntry(parameters);
        TargetToken target = _app.TakeWebTarget()
            ?? throw new InvalidOperationException("A janela de destino nao esta mais disponivel.");
        CloseWindowRequested?.Invoke();
        TextDeliveryResult result = await _app.RepasteAsync(entry.Text, target).ConfigureAwait(false);
        if (result != TextDeliveryResult.Delivered)
        {
            string message = $"Nao foi possivel recolar o texto: {result}.";
            _app.ShowWebError(message);
            throw new InvalidOperationException(message);
        }
        return new { result = result.ToString().ToLowerInvariant() };
    }

    private async Task<object> CaptureHotkeyAsync()
    {
        if (_app == null)
            throw new InvalidOperationException("O teclado do Matraca nao esta disponivel.");

        CancellationTokenSource cancellation;
        lock (_hotkeyCaptureGate)
        {
            _hotkeyCaptureCancellation?.Cancel();
            _hotkeyCaptureCancellation?.Dispose();
            cancellation = new CancellationTokenSource();
            _hotkeyCaptureCancellation = cancellation;
        }
        try
        {
            HotkeyGesture gesture = await _app.CaptureHotkeyAsync(cancellation.Token)
                .ConfigureAwait(false);
            return new { hotkey = gesture.ToString() };
        }
        finally
        {
            lock (_hotkeyCaptureGate)
            {
                if (ReferenceEquals(_hotkeyCaptureCancellation, cancellation))
                    _hotkeyCaptureCancellation = null;
            }
            cancellation.Dispose();
        }
    }

    private object CancelHotkeyCapture()
    {
        lock (_hotkeyCaptureGate) _hotkeyCaptureCancellation?.Cancel();
        return new { canceled = true };
    }

    private static object BuildModels()
    {
        HashSet<string> existing = ModelDownloader.Existing(MacConfig.Paths)
            .Select(Path.GetFullPath)
            .ToHashSet(StringComparer.Ordinal);
        return new
        {
            entries = ModelDownloader.Catalog.Select(model =>
            {
                string path = ModelDownloader.PathFor(model, MacConfig.Paths);
                return new
                {
                    id = model.FileName,
                    label = model.Label,
                    bytes = model.Bytes,
                    downloaded = existing.Contains(Path.GetFullPath(path))
                        && new FileInfo(path).Length == model.Bytes,
                    path,
                };
            }).ToArray(),
        };
    }

    private Task<object> DownloadModelAsync(JsonElement parameters)
    {
        MacTrayApp app = _app
            ?? throw new InvalidOperationException("O runtime do Matraca nao esta disponivel.");
        if (parameters.ValueKind != JsonValueKind.Object
            || !parameters.TryGetProperty("id", out JsonElement idElement)
            || idElement.ValueKind != JsonValueKind.String)
            throw new JsonException("model.download.start exige params.id.");
        string id = idElement.GetString()!;
        ModelInfo model = ModelDownloader.Catalog.FirstOrDefault(item => item.FileName == id)
            ?? throw new JsonException("O modelo solicitado nao pertence ao catalogo.");

        CancellationTokenSource cancellation;
        Task<object> task;
        lock (_modelDownloadGate)
        {
            if (Volatile.Read(ref _stopping) != 0 || Volatile.Read(ref _disposed) != 0)
                throw new InvalidOperationException("O Matraca esta encerrando.");
            if (_modelDownloadCancellation != null)
                throw new InvalidOperationException("Ja existe um download de modelo em andamento.");
            cancellation = new CancellationTokenSource();
            _modelDownloadCancellation = cancellation;
            task = Task.Run(() => DownloadModelCoreAsync(app, model, cancellation));
            _modelDownloadTask = task;
        }
        return task;
    }

    private async Task<object> DownloadModelCoreAsync(
        MacTrayApp app,
        ModelInfo model,
        CancellationTokenSource cancellation)
    {
        try
        {
            string path = ModelDownloader.PathFor(model, MacConfig.Paths);
            if (File.Exists(path) && new FileInfo(path).Length != model.Bytes)
                File.Delete(path);
            if (!File.Exists(path))
            {
                var progress = new Progress<(long done, long total)>(value => Emit(
                    "model.download.progress",
                    new { id = model.FileName, value.done, value.total }));
                path = await ModelDownloader.DownloadAsync(
                        model,
                        progress,
                        cancellation.Token,
                        MacConfig.Paths)
                    .ConfigureAwait(false);
            }
            (RawConfig raw, bool restartRequired) = await app.ApplyAndSaveConfigPatchAsync(
                    JsonSerializer.Serialize(new { modelPath = path }))
                .ConfigureAwait(false);
            return new
            {
                path,
                config = BuildPublicConfig(raw),
                restartRequired,
                models = BuildModels(),
            };
        }
        finally
        {
            lock (_modelDownloadGate)
            {
                if (ReferenceEquals(_modelDownloadCancellation, cancellation))
                {
                    _modelDownloadCancellation = null;
                    _modelDownloadTask = null;
                }
            }
            cancellation.Dispose();
        }
    }

    private object CancelModelDownload()
    {
        lock (_modelDownloadGate) _modelDownloadCancellation?.Cancel();
        return new { canceled = true };
    }

    private DictationHistoryEntry FindHistoryEntry(JsonElement parameters)
    {
        if (_app == null)
            throw new InvalidOperationException("O historico nao esta disponivel.");
        if (parameters.ValueKind != JsonValueKind.Object
            || !parameters.TryGetProperty("id", out JsonElement idElement)
            || idElement.ValueKind != JsonValueKind.String)
            throw new JsonException("O comando de historico exige params.id.");
        string id = idElement.GetString()!;
        return _app.HistorySnapshot().FirstOrDefault(entry => HistoryId(entry) == id)
            ?? throw new InvalidOperationException("O item do historico nao existe mais.");
    }

    private async Task<object> StartMicrophoneAsync(JsonElement parameters)
    {
        int generation = Interlocked.Increment(ref _microphoneGeneration);
        string device = parameters.ValueKind == JsonValueKind.Object
            && parameters.TryGetProperty("device", out JsonElement deviceElement)
            && deviceElement.ValueKind == JsonValueKind.String
                ? deviceElement.GetString()!.Trim()
                : _app?.CurrentConfig.InputDevice ?? "";
        await _microphoneGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (generation != Volatile.Read(ref _microphoneGeneration)
                || Volatile.Read(ref _stopping) != 0)
                return new { started = false, devices = Array.Empty<string>(), currentDevice = device, threshold = CurrentThreshold };
            await _microphone.StopAsync().ConfigureAwait(false);
            _monitoredDevice = device;
            Array.Clear(_smoothedBands);
            _peak = 0;
            await _microphone.StartAsync(device).ConfigureAwait(false);
            if (generation != Volatile.Read(ref _microphoneGeneration))
            {
                await _microphone.StopAsync().ConfigureAwait(false);
                return new { started = false, devices = Array.Empty<string>(), currentDevice = device, threshold = CurrentThreshold };
            }
            return new
            {
                started = true,
                devices = _microphone.ListDevices(),
                currentDevice = device,
                threshold = CurrentThreshold,
            };
        }
        finally { _microphoneGate.Release(); }
    }

    private async Task<object> StopMicrophoneAsync()
    {
        Interlocked.Increment(ref _microphoneGeneration);
        await _microphoneGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await _microphone.StopAsync().ConfigureAwait(false);
            return new { stopped = true };
        }
        finally { _microphoneGate.Release(); }
    }

    private object BuildPermissions() => new
    {
        accessibility = Accessibility.IsTrusted(prompt: false) ? "granted" : "denied",
        microphone = _microphone.IsRunning ? "granted" : "unknown",
    };

    private static object OpenPermissionSettings(JsonElement parameters)
    {
        string name = parameters.ValueKind == JsonValueKind.Object
            && parameters.TryGetProperty("name", out JsonElement nameElement)
            && nameElement.ValueKind == JsonValueKind.String
                ? nameElement.GetString()!
                : "accessibility";
        string pane = name == "microphone" ? "Privacy_Microphone" : "Privacy_Accessibility";
        IntPtr workspace = ObjC.Send(ObjCClasses.NSWorkspace, ObjCSelectors.SharedWorkspace);
        IntPtr url = ObjC.Send(
            ObjCClasses.NSURL,
            ObjCSelectors.URLWithString,
            NSStringRef.From($"x-apple.systempreferences:com.apple.preference.security?{pane}"));
        bool opened = workspace != IntPtr.Zero
            && url != IntPtr.Zero
            && ObjC.SendBool(workspace, ObjCSelectors.OpenURL, url);
        return new { opened };
    }

    private object BuildState() => new
    {
        state = StateName(_app?.CurrentState ?? ShellState.Idle),
        text = _app?.CurrentStateText ?? "Acessibilidade necessaria.",
    };

    private float CurrentThreshold
        => _app?.CurrentConfig.VadThresholdFor(_monitoredDevice) ?? 0.012f;

    private void OnMicrophoneFrame(float rms, float peak, float[] bands)
    {
        for (int index = 0; index < _smoothedBands.Length; index++)
            _smoothedBands[index] = Math.Max(bands[index], _smoothedBands[index] * 0.82f);
        _peak = Math.Max(peak, _peak * 0.94f);
        float threshold = CurrentThreshold;
        Emit("mic.frame", new
        {
            device = _monitoredDevice,
            rms,
            peak = _peak,
            threshold,
            speech = rms > threshold,
            bands = _smoothedBands,
        });
    }

    private void OnStateChanged(ShellState state, string text)
        => Emit("hud.state", new
        {
            state = StateName(state),
            title = text,
            text,
            detail = state == ShellState.Recording ? _app?.CurrentConfig.Mode : "",
        });

    private void OnConfigChanged(Matraca.Core.Config config)
    {
        try
        {
            RawConfig raw = _app?.LoadRawConfig() ?? MacConfig.LoadRaw();
            Emit("config.changed", new { config = BuildPublicConfig(raw) });
        }
        catch (Exception exception) { Logger.Error("Falha ao publicar configuracao para a UI", exception); }
    }

    private static object BuildPublicConfig(RawConfig raw)
    {
        JsonElement serialized = JsonSerializer.SerializeToElement(raw, ReadOptions);
        var result = new Dictionary<string, object?>();
        foreach (JsonProperty property in serialized.EnumerateObject())
        {
            if (property.Name == nameof(RawConfig.postProcessApiKey)) continue;
            result[property.Name] = property.Value.Clone();
        }
        result["postProcessApiKeyConfigured"] = !string.IsNullOrWhiteSpace(raw.postProcessApiKey);
        return result;
    }

    private void Emit(string type, object payload)
    {
        try
        {
            MessageProduced?.Invoke(JsonSerializer.Serialize(new
            {
                version = 1,
                type,
                payload,
            }));
        }
        catch (Exception exception)
        {
            Logger.Error("Falha ao publicar evento para a interface web", exception);
        }
    }

    private static string StateName(ShellState state) => state switch
    {
        ShellState.Recording => "listening",
        ShellState.Busy => "thinking",
        ShellState.Error => "error",
        _ => "ready",
    };

    private static string HistoryId(DictationHistoryEntry entry)
    {
        byte[] bytes = Encoding.UTF8.GetBytes($"{entry.At.ToBinary()}\n{entry.Text}");
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static string Response(string id, object result)
        => JsonSerializer.Serialize(new { version = 1, id, type = "response", ok = true, result });

    private static string ErrorResponse(string? id, string code, string message)
        => JsonSerializer.Serialize(new
        {
            version = 1,
            id,
            type = "response",
            ok = false,
            error = new { code, message },
        });

    private static string? GetRequestId(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("id", out JsonElement id)
                && id.ValueKind == JsonValueKind.String
                    ? id.GetString()
                    : null;
        }
        catch { return null; }
    }

    private static void Observe(Task task)
        => _ = task.ContinueWith(
            failed => Logger.Error(
                "Falha ao encerrar monitor de microfone",
                failed.Exception!.GetBaseException()),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
}
