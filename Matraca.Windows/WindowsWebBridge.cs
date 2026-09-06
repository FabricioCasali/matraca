using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Matraca;

internal sealed class WindowsWebBridge : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly IWindowsWebBridgeApp _app;
    private readonly WindowsMicrophoneMonitor _microphone;
    private readonly SemaphoreSlim _microphoneGate = new(1, 1);
    private readonly object _hotkeyCaptureGate = new();
    private readonly object _modelDownloadGate = new();
    private readonly float[] _smoothedBands = new float[SpectrumAnalyzer.BandCount];
    private string _monitoredDevice = "";
    private float _peak;
    private int _microphoneGeneration;
    private int _microphoneSuspendedController;
    private CancellationTokenSource? _hotkeyCaptureCancellation;
    private CancellationTokenSource? _modelDownloadCancellation;
    private Task<object>? _modelDownloadTask;
    private int _stopping;
    private int _disposed;

    public WindowsWebBridge(IWindowsWebBridgeApp app, IShell shell)
    {
        _app = app ?? throw new ArgumentNullException(nameof(app));
        _microphone = new WindowsMicrophoneMonitor(shell);
        _microphone.Frame += OnMicrophoneFrame;
        _app.StateChanged += OnStateChanged;
        _app.ConfigChanged += OnConfigChanged;
        _app.DeliveryCompleted += OnDeliveryCompleted;
    }

    public event Action<string>? MessageProduced;
    public event Action? CloseWindowRequested;
    public event Action? MinimizeWindowRequested;
    public event Action? ToggleMaximizeWindowRequested;
    public event Action? DragWindowRequested;

    public void PrepareToOpen(IntPtr excludedWindow)
    {
        _app.CaptureWebTarget(excludedWindow);
        Emit("window.opened", new { });
    }

    public void WindowClosed()
    {
        CancelHotkeyCapture();
        _app.ReleaseWebTarget();
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
                "config.set" => await SetConfigAsync(parameters),
                "history.list" => BuildHistory(),
                "history.delete" => DeleteHistory(parameters),
                "history.clear" => ClearHistory(parameters),
                "history.copy" => CopyHistory(parameters),
                "history.repaste" => await RepasteHistoryAsync(parameters),
                "file.pick" => await PickFileAsync(parameters),
                "sound.preview" => await PreviewSoundAsync(parameters),
                "hotkey.capture.start" => await CaptureHotkeyAsync(),
                "hotkey.capture.cancel" => CancelHotkeyCapture(),
                "mic.monitor.start" => await StartMicrophoneAsync(parameters),
                "mic.monitor.stop" => await StopMicrophoneAsync(),
                "model.download.start" => await DownloadModelAsync(parameters),
                "model.download.cancel" => CancelModelDownload(),
                "dictation.toggle" => await ToggleDictationAsync(),
                "permissions.get" => BuildPermissions(),
                "permissions.open-settings" => OpenPermissionSettings(parameters),
                "window.minimize" => RequestWindowMinimize(),
                "window.toggleMaximize" => RequestWindowToggleMaximize(),
                "window.close" => RequestWindowClose(),
                "window.drag" => RequestWindowDrag(),
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
            Logger.Error("Falha ao executar comando da interface WebView2", exception);
            return ErrorResponse(GetRequestId(json), "native_error", exception.Message);
        }
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _stopping, 1) != 0) return;
        CancelHotkeyCapture();
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

    private object BuildSnapshot() => new
    {
        platform = "windows",
        capabilities = new
        {
            filePick = true,
            soundPreview = true,
            historyClear = true,
        },
        config = BuildConfig(),
        history = BuildHistory(),
        state = BuildState(),
        permissions = BuildPermissions(),
        devices = _app.ListAudioDevices(),
        models = BuildModels(),
    };

    private object BuildConfig()
    {
        RawConfig raw = _app.LoadRawConfig();
        return new
        {
            config = BuildPublicConfig(raw),
            runtime = new
            {
                gpu = _app.RuntimeGpu,
                desiredGpu = _app.CurrentConfig.Gpu,
                restartRequired = _app.CurrentConfig.Gpu != _app.RuntimeGpu,
                postProcessActive = _app.PostProcessingActive,
            },
        };
    }

    private async Task<object> SetConfigAsync(JsonElement parameters)
    {
        if (parameters.ValueKind != JsonValueKind.Object
            || !parameters.TryGetProperty("patch", out JsonElement patch)
            || patch.ValueKind != JsonValueKind.Object)
            throw new JsonException("config.set exige params.patch.");

        (RawConfig raw, bool restartRequired) = await _app.ApplyAndSaveConfigPatchAsync(
            patch.GetRawText());
        return new
        {
            config = BuildPublicConfig(raw),
            restartRequired,
            postProcessActive = _app.PostProcessingActive,
        };
    }

    private object BuildHistory() => new
    {
        entries = _app.HistorySnapshot().Select(entry => new
        {
            id = HistoryId(entry),
            at = entry.At,
            text = entry.Text,
            characterCount = entry.Text.Length,
        }).ToArray(),
    };

    private object DeleteHistory(JsonElement parameters)
    {
        DictationHistoryEntry entry = FindHistoryEntry(parameters);
        if (!_app.RemoveHistory(entry.At, entry.Text))
            throw new IOException("Não foi possível persistir a exclusão do histórico.");
        return new { deleted = true };
    }

    private object ClearHistory(JsonElement parameters)
    {
        RequireExactProperties(parameters);
        _app.ClearHistory();
        return new { cleared = true };
    }

    private async Task<object> PickFileAsync(JsonElement parameters)
    {
        RequireExactProperties(parameters, "kind");
        JsonElement kindElement = parameters.GetProperty("kind");
        if (kindElement.ValueKind != JsonValueKind.String)
            throw new JsonException("file.pick exige params.kind como string.");
        string kind = kindElement.GetString()!;
        if (kind is not ("model" or "sound"))
            throw new JsonException("file.pick aceita kind model ou sound.");
        string? path = await _app.PickFileAsync(kind);
        return new { path };
    }

    private async Task<object> PreviewSoundAsync(JsonElement parameters)
    {
        RequireExactProperties(parameters, "start", "path");
        JsonElement startElement = parameters.GetProperty("start");
        JsonElement pathElement = parameters.GetProperty("path");
        if (startElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
            || pathElement.ValueKind != JsonValueKind.String)
            throw new JsonException("sound.preview exige params.start booleano e params.path string.");
        int durationMs = await _app.PreviewSoundAsync(
            startElement.GetBoolean(),
            pathElement.GetString());
        return new { played = durationMs > 0, durationMs };
    }

    private object CopyHistory(JsonElement parameters)
    {
        DictationHistoryEntry entry = FindHistoryEntry(parameters);
        if (!_app.CopyText(entry.Text))
            throw new IOException("O clipboard recusou a cópia.");
        return new { copied = true };
    }

    private async Task<object> RepasteHistoryAsync(JsonElement parameters)
    {
        DictationHistoryEntry entry = FindHistoryEntry(parameters);
        TargetToken target = _app.TakeWebTarget()
            ?? throw new InvalidOperationException("A janela de destino não está mais disponível.");
        CloseWindowRequested?.Invoke();
        TextDeliveryResult result = await _app.RepasteAsync(entry.Text, target).ConfigureAwait(false);
        if (result != TextDeliveryResult.Delivered)
        {
            string message = $"Não foi possível recolar o texto: {result}.";
            _app.ShowWebError(message);
            throw new InvalidOperationException(message);
        }
        return new { result = result.ToString().ToLowerInvariant() };
    }

    private async Task<object> CaptureHotkeyAsync()
    {
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
            using var capture = new WindowsHotkeyCapture(cancellation.Token);
            HotkeyGesture gesture = await capture.Completion.ConfigureAwait(false);
            if (!HotkeyParser.TryParse(gesture.ToString(), out HotkeyGesture validated))
                throw new InvalidOperationException("O atalho capturado não é válido.");
            return new { hotkey = validated.ToString() };
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

    private object RequestWindowMinimize()
    {
        MinimizeWindowRequested?.Invoke();
        return new { requested = true };
    }

    private object RequestWindowToggleMaximize()
    {
        ToggleMaximizeWindowRequested?.Invoke();
        return new { requested = true };
    }

    private object RequestWindowClose()
    {
        CloseWindowRequested?.Invoke();
        return new { requested = true };
    }

    private object RequestWindowDrag()
    {
        DragWindowRequested?.Invoke();
        return new { requested = true };
    }

    private async Task<object> ToggleDictationAsync()
    {
        await _app.ToggleDictationFromUiAsync(_app.CurrentWebTarget).ConfigureAwait(false);
        return BuildState();
    }

    private object CancelHotkeyCapture()
    {
        lock (_hotkeyCaptureGate) _hotkeyCaptureCancellation?.Cancel();
        return new { canceled = true };
    }

    private object BuildModels()
    {
        HashSet<string> existing = ModelDownloader.Existing(WindowsConfig.Paths)
            .Select(Path.GetFullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new
        {
            entries = ModelDownloader.Catalog.Select(model =>
            {
                string path = ModelDownloader.PathFor(model, WindowsConfig.Paths);
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
        if (parameters.ValueKind != JsonValueKind.Object
            || !parameters.TryGetProperty("id", out JsonElement idElement)
            || idElement.ValueKind != JsonValueKind.String)
            throw new JsonException("model.download.start exige params.id.");
        string id = idElement.GetString()!;
        ModelInfo model = ModelDownloader.Catalog.FirstOrDefault(item => item.FileName == id)
            ?? throw new JsonException("O modelo solicitado não pertence ao catálogo.");

        CancellationTokenSource cancellation;
        Task<object> task;
        lock (_modelDownloadGate)
        {
            if (Volatile.Read(ref _stopping) != 0 || Volatile.Read(ref _disposed) != 0)
                throw new InvalidOperationException("O Matraca está encerrando.");
            if (_modelDownloadCancellation != null)
                throw new InvalidOperationException("Já existe um download de modelo em andamento.");
            cancellation = new CancellationTokenSource();
            _modelDownloadCancellation = cancellation;
            task = Task.Run(() => DownloadModelCoreAsync(model, cancellation));
            _modelDownloadTask = task;
        }
        return task;
    }

    private async Task<object> DownloadModelCoreAsync(
        ModelInfo model,
        CancellationTokenSource cancellation)
    {
        try
        {
            string path = ModelDownloader.PathFor(model, WindowsConfig.Paths);
            if (File.Exists(path) && new FileInfo(path).Length != model.Bytes) File.Delete(path);
            if (!File.Exists(path))
            {
                var progress = new Progress<(long done, long total)>(value => Emit(
                    "model.download.progress",
                    new { id = model.FileName, value.done, value.total }));
                path = await ModelDownloader.DownloadAsync(
                    model,
                    progress,
                    cancellation.Token,
                    WindowsConfig.Paths).ConfigureAwait(false);
            }
            (RawConfig raw, bool restartRequired) = await _app.ApplyAndSaveConfigPatchAsync(
                JsonSerializer.Serialize(new { modelPath = path })).ConfigureAwait(false);
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

    private async Task<object> StartMicrophoneAsync(JsonElement parameters)
    {
        int generation = Interlocked.Increment(ref _microphoneGeneration);
        string device = parameters.ValueKind == JsonValueKind.Object
            && parameters.TryGetProperty("device", out JsonElement deviceElement)
            && deviceElement.ValueKind == JsonValueKind.String
                ? deviceElement.GetString()!.Trim()
                : _app.CurrentConfig.InputDevice;
        await _microphoneGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (generation != Volatile.Read(ref _microphoneGeneration)
                || Volatile.Read(ref _stopping) != 0)
                return new { started = false, currentDevice = device, threshold = CurrentThreshold };
            await _microphone.StopAsync().ConfigureAwait(false);
            if (Volatile.Read(ref _microphoneSuspendedController) == 0)
            {
                await _app.BeginMicrophoneMonitorAsync().ConfigureAwait(false);
                Volatile.Write(ref _microphoneSuspendedController, 1);
            }
            if (generation != Volatile.Read(ref _microphoneGeneration))
            {
                if (Interlocked.Exchange(ref _microphoneSuspendedController, 0) != 0)
                    await _app.EndMicrophoneMonitorAsync().ConfigureAwait(false);
                return new { started = false, currentDevice = device, threshold = CurrentThreshold };
            }
            _monitoredDevice = device;
            Array.Clear(_smoothedBands);
            _peak = 0;
            try { await _microphone.StartAsync(device).ConfigureAwait(false); }
            catch
            {
                if (Interlocked.Exchange(ref _microphoneSuspendedController, 0) != 0)
                    await _app.EndMicrophoneMonitorAsync().ConfigureAwait(false);
                throw;
            }
            if (generation != Volatile.Read(ref _microphoneGeneration))
            {
                await _microphone.StopAsync().ConfigureAwait(false);
                return new { started = false, currentDevice = device, threshold = CurrentThreshold };
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
            try { await _microphone.StopAsync().ConfigureAwait(false); }
            finally
            {
                if (Interlocked.Exchange(ref _microphoneSuspendedController, 0) != 0)
                    await _app.EndMicrophoneMonitorAsync().ConfigureAwait(false);
            }
            return new { stopped = true };
        }
        finally { _microphoneGate.Release(); }
    }

    private object BuildPermissions() => new
    {
        accessibility = "granted",
        microphone = _microphone.IsRunning ? "granted" : "unknown",
    };

    private static object OpenPermissionSettings(JsonElement parameters)
    {
        string name = parameters.ValueKind == JsonValueKind.Object
            && parameters.TryGetProperty("name", out JsonElement nameElement)
            && nameElement.ValueKind == JsonValueKind.String
                ? nameElement.GetString()!
                : "microphone";
        string uri = name == "microphone" ? "ms-settings:privacy-microphone" : "ms-settings:easeofaccess";
        Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
        return new { opened = true };
    }

    private object BuildState() => new
    {
        state = StateName(_app.CurrentState),
        text = _app.CurrentStateText,
        active = _app.CurrentState == ShellState.Recording,
    };

    private DictationHistoryEntry FindHistoryEntry(JsonElement parameters)
    {
        if (parameters.ValueKind != JsonValueKind.Object
            || !parameters.TryGetProperty("id", out JsonElement idElement)
            || idElement.ValueKind != JsonValueKind.String)
            throw new JsonException("O comando de histórico exige params.id.");
        string id = idElement.GetString()!;
        return _app.HistorySnapshot().FirstOrDefault(entry => HistoryId(entry) == id)
            ?? throw new InvalidOperationException("O item do histórico não existe mais.");
    }

    private float CurrentThreshold => _app.CurrentConfig.VadThresholdFor(_monitoredDevice);

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
            detail = state == ShellState.Recording ? _app.CurrentConfig.Mode : "",
        });

    private void OnConfigChanged(Config config)
    {
        try
        {
            Emit("config.changed", new
            {
                config = BuildPublicConfig(_app.LoadRawConfig()),
                postProcessActive = _app.PostProcessingActive,
            });
        }
        catch (Exception exception) { Logger.Error("Falha ao publicar configuração para a UI", exception); }
    }

    private void OnDeliveryCompleted(string text, TextDeliveryResult result, bool streaming)
        => Emit("dictation.completed", new
        {
            text,
            at = DateTime.Now,
            history = BuildHistory(),
        });

    private static object BuildPublicConfig(RawConfig raw)
    {
        JsonElement serialized = JsonSerializer.SerializeToElement(raw, JsonOptions);
        var result = new Dictionary<string, object?>();
        foreach (JsonProperty property in serialized.EnumerateObject())
        {
            if (property.Name is nameof(RawConfig.postProcessApiKey)
                or nameof(RawConfig.postProcessOpenAiApiKey)
                or nameof(RawConfig.postProcessDeepSeekApiKey)) continue;
            result[property.Name] = property.Value.Clone();
        }
        string provider = Config.NormalizePostProcessProvider(raw.postProcessProvider);
        result["postProcessProvider"] = provider;
        result["postProcessApiKeyConfigured"] = !string.IsNullOrWhiteSpace(provider switch
        {
            "openai-compatible" => raw.postProcessOpenAiApiKey,
            "deepseek" => raw.postProcessDeepSeekApiKey,
            _ => raw.postProcessApiKey,
        });
        return result;
    }

    private static void RequireExactProperties(JsonElement parameters, params string[] names)
    {
        if (parameters.ValueKind != JsonValueKind.Object)
            throw new JsonException("O comando exige params como objeto.");
        JsonProperty[] properties = parameters.EnumerateObject().ToArray();
        if (properties.Length != names.Length
            || properties.Any(property => !names.Contains(property.Name, StringComparer.Ordinal)))
            throw new JsonException("Os parâmetros do comando não correspondem ao contrato.");
    }

    private void Emit(string type, object payload)
    {
        try
        {
            MessageProduced?.Invoke(JsonSerializer.Serialize(new { version = 1, type, payload }));
        }
        catch (Exception exception)
        {
            Logger.Error("Falha ao publicar evento para a interface WebView2", exception);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        ShutdownAsync().GetAwaiter().GetResult();
        _app.StateChanged -= OnStateChanged;
        _app.ConfigChanged -= OnConfigChanged;
        _app.DeliveryCompleted -= OnDeliveryCompleted;
        _app.ReleaseWebTarget();
        _microphone.Frame -= OnMicrophoneFrame;
        _microphone.Dispose();
        MessageProduced = null;
        CloseWindowRequested = null;
        MinimizeWindowRequested = null;
        ToggleMaximizeWindowRequested = null;
        DragWindowRequested = null;
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
                "Falha ao encerrar recurso da interface WebView2",
                failed.Exception!.GetBaseException()),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
}
