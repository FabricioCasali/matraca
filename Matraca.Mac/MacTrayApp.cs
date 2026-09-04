using Matraca.Core;
using Matraca.Mac.Config;
using Matraca.Mac.Platform;
using Matraca.Mac.Platform.Audio;
using Matraca.Mac.Platform.Interop;
using Matraca.Mac.Platform.Keyboard;
using Matraca.Mac.Platform.Speech;
using Matraca.Mac.Platform.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CoreConfig = Matraca.Core.Config;

namespace Matraca.Mac;

internal sealed class MacTrayApp : IDisposable
{
    private readonly DictationController _controller;
    private readonly SemaphoreSlim _configGate = new(1, 1);
    private readonly MacShell _shell;
    private readonly MacAudioCapture _audio;
    private readonly MacTargetWindow _targets;
    private readonly object _webTargetGate = new();
    private readonly string _runtimeGpu;
    private CoreConfig _config;
    private TargetToken? _webTarget;
    private MacConfigWatcher? _configWatcher;
    private MacSleepWakeMonitor? _sleepWakeMonitor;
    private int _powerGeneration;
    private int _sleeping;
    private int _stopping;

    public MacTrayApp(CoreConfig config, MacStatusItem statusItem)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _runtimeGpu = config.Gpu;
        ArgumentNullException.ThrowIfNull(statusItem);

        var keyboard = new MacKeyboardHook(config);
        _audio = new MacAudioCapture();
        _targets = new MacTargetWindow();
        _shell = new MacShell(statusItem);
        _controller = new DictationController(
            config,
            keyboard,
            _audio,
            new MacTextSink(_targets),
            _targets,
            _shell,
            new TranscriptionModelManager(config, MacWhisperTranscriber.CreateModelAsync),
            TextPostProcessor.TryCreate,
            next => next.History
                ? new DictationHistory(MacConfig.Paths, next.HistoryMaxItems)
                : null,
            MainThread.Post);
    }

    public void Start()
    {
        _controller.Start();
        _sleepWakeMonitor = new MacSleepWakeMonitor(OnWillSleep, OnDidWake);
        _configWatcher = new MacConfigWatcher();
        _configWatcher.Changed += OnConfigChanged;
    }

    internal event Action<ShellState, string>? StateChanged
    {
        add => _shell.StateChanged += value;
        remove => _shell.StateChanged -= value;
    }

    internal event Action<CoreConfig>? ConfigChanged;

    internal CoreConfig CurrentConfig => _config;
    internal ShellState CurrentState => _shell.CurrentState;
    internal string CurrentStateText => _shell.CurrentText;
    internal RawConfig LoadRawConfig() => MacConfig.LoadRaw();
    internal IReadOnlyList<string> ListAudioDevices() => _audio.ListDevices();
    internal List<DictationHistoryEntry> HistorySnapshot()
        => _controller.CurrentHistory?.Snapshot() ?? [];

    internal async Task<(RawConfig Config, bool RestartRequired)> ApplyAndSaveConfigPatchAsync(
        string patchJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(patchJson);
        await _configGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _stopping) != 0)
                throw new InvalidOperationException("O Matraca esta encerrando.");
            RawConfig current = MacConfig.LoadRaw();
            JsonObject merged = JsonSerializer.SerializeToNode(current)?.AsObject()
                ?? throw new JsonException("A configuracao atual nao pode ser serializada.");
            JsonObject patch = JsonNode.Parse(patchJson)?.AsObject()
                ?? throw new JsonException("config.set exige um patch JSON.");
            foreach ((string name, JsonNode? value) in patch)
            {
                if (name == "postProcessApiKeyConfigured") continue;
                merged[name] = value?.DeepClone();
            }
            RawConfig raw = merged.Deserialize<RawConfig>()
                ?? throw new JsonException("O patch produziu uma configuracao vazia.");
            CoreConfig previous = _config;
            CoreConfig next = CoreConfig.FromRaw(raw, warning: Logger.Warn, paths: MacConfig.Paths);
            await ApplyConfigLockedAsync(next, notifyChanged: false, notifyRestart: false)
                .ConfigureAwait(false);
            try
            {
                MacConfig.SaveRaw(raw);
            }
            catch
            {
                await ApplyConfigLockedAsync(previous, notifyChanged: false, notifyRestart: false)
                    .ConfigureAwait(false);
                throw;
            }
            ConfigChanged?.Invoke(next);
            bool restartRequired = next.Gpu != _runtimeGpu;
            if (restartRequired) NotifyRestartRequired();
            return (raw, restartRequired);
        }
        catch (Exception exception)
        {
            NotifyConfigRejected(exception);
            throw;
        }
        finally { _configGate.Release(); }
    }

    internal bool RemoveHistory(DateTime at, string text)
        => _controller.CurrentHistory?.Remove(at, text) == true;

    internal bool CopyText(string text) => new MacPasteboard().WriteText(text);

    internal void ShowWebError(string message)
        => MainThread.Post(() => _shell.ShowNotification(
            "Operacao da interface falhou",
            message,
            ShellNotificationLevel.Error));

    internal void CaptureWebTarget()
    {
        TargetToken? next = _targets.CaptureActive();
        TargetToken? previous;
        lock (_webTargetGate)
        {
            previous = _webTarget;
            _webTarget = next;
        }
        if (previous != null) _targets.Release(previous);
    }

    internal void ReleaseWebTarget()
    {
        TargetToken? target;
        lock (_webTargetGate)
        {
            target = _webTarget;
            _webTarget = null;
        }
        if (target != null) _targets.Release(target);
    }

    internal TargetToken? TakeWebTarget()
    {
        lock (_webTargetGate)
        {
            TargetToken? target = _webTarget;
            _webTarget = null;
            return target;
        }
    }

    internal async Task<TextDeliveryResult> RepasteAsync(string text, TargetToken target)
    {
        try
        {
            return await _controller.Delivery.EnqueueAsync(new TextDeliveryRequest(
                    text,
                    false,
                    TextDeliveryMethod.TargetWithFocus,
                    target))
                .ConfigureAwait(false);
        }
        finally
        {
            _targets.Release(target);
        }
    }

    private void OnWillSleep()
    {
        if (Volatile.Read(ref _stopping) != 0
            || Interlocked.Exchange(ref _sleeping, 1) != 0)
            return;

        Interlocked.Increment(ref _powerGeneration);
        Logger.Info("macOS vai entrar em repouso.");
        try { Observe(_controller.SuspendAsync()); }
        catch (Exception exception) { Logger.Error("Falha ao iniciar suspensao", exception); }
    }

    private void OnDidWake()
    {
        if (Volatile.Read(ref _stopping) != 0
            || Interlocked.CompareExchange(ref _sleeping, 0, 1) != 1)
            return;

        int generation = Volatile.Read(ref _powerGeneration);
        Logger.Info("macOS retomou do repouso.");
        Observe(WakeAsync(generation));
    }

    private async Task WakeAsync(int generation)
    {
        try
        {
            await ApplyConfigAsync(MacConfig.Load()).ConfigureAwait(false);
        }
        catch
        {
            // ApplyConfigAsync already reports the rejected configuration.
        }

        if (Volatile.Read(ref _stopping) != 0
            || Volatile.Read(ref _sleeping) != 0
            || Volatile.Read(ref _powerGeneration) != generation)
            return;

        MainThread.Post(() => ResumeOnMainThread(generation));
    }

    private void ResumeOnMainThread(int generation)
    {
        if (Volatile.Read(ref _stopping) != 0
            || Volatile.Read(ref _sleeping) != 0
            || Volatile.Read(ref _powerGeneration) != generation)
            return;

        try { Observe(_controller.ResumeAsync()); }
        catch (Exception exception)
        {
            Interlocked.CompareExchange(ref _sleeping, 1, 0);
            Logger.Error("Falha ao recriar o event tap apos repouso", exception);
            _shell.SetState(
                ShellState.Error,
                "Matraca - teclado indisponivel apos repouso.");
        }
    }

    private void OnConfigChanged(CoreConfig config)
    {
        if (Volatile.Read(ref _stopping) == 0)
            MainThread.Post(() => Observe(ApplyConfigAsync(config)));
    }

    private async Task ApplyConfigAsync(CoreConfig config, bool notifyChanged = true)
    {
        await _configGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _stopping) != 0) return;
            await ApplyConfigLockedAsync(config, notifyChanged, notifyRestart: true)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            NotifyConfigRejected(exception);
            throw;
        }
        finally
        {
            _configGate.Release();
        }
    }

    private async Task ApplyConfigLockedAsync(
        CoreConfig config,
        bool notifyChanged,
        bool notifyRestart)
    {
        CoreConfig previous = _config;
        bool accelerationChanged = config.Gpu != previous.Gpu;
        CoreConfig applicable = config.Gpu == _runtimeGpu ? config : config.WithGpu(_runtimeGpu);
        bool keyboardChanged = config.Hotkey != previous.Hotkey
            || config.PinHotkey != previous.PinHotkey
            || config.DiscoverMode != previous.DiscoverMode;
        if (keyboardChanged)
        {
            try
            {
                await _controller.ApplyConfigAsync(applicable, new MacKeyboardHook(applicable))
                    .ConfigureAwait(false);
            }
            catch
            {
                try
                {
                    await _controller.ApplyConfigAsync(previous, new MacKeyboardHook(previous))
                        .ConfigureAwait(false);
                }
                catch (Exception restoreException)
                {
                    Logger.Error("Falha ao restaurar o event tap anterior", restoreException);
                    MainThread.Post(() => _shell.SetState(
                        ShellState.Error,
                        "Matraca - teclado indisponivel; confira Acessibilidade."));
                }
                throw;
            }
        }
        else
        {
            await _controller.ApplyConfigAsync(applicable).ConfigureAwait(false);
        }
        _config = config;
        if (notifyChanged) ConfigChanged?.Invoke(config);
        if (notifyRestart && accelerationChanged) NotifyRestartRequired();
    }

    private void NotifyRestartRequired()
        => MainThread.Post(() => _shell.ShowNotification(
            "Reinicie o Matraca",
            "A troca entre GPU e CPU passa a valer na proxima inicializacao.",
            ShellNotificationLevel.Warning));

    private void NotifyConfigRejected(Exception exception)
        => MainThread.Post(() => _shell.ShowNotification(
            "Configuracao rejeitada",
            exception.Message,
            ShellNotificationLevel.Error));

    private static void Observe(Task task)
        => _ = task.ContinueWith(
            failed => Logger.Error(
                "Falha ao aplicar configuracoes do Mac",
                failed.Exception!.GetBaseException()),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);

    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _stopping, 1) != 0) return;
        ReleaseWebTarget();
        _sleepWakeMonitor?.Dispose();
        _sleepWakeMonitor = null;
        if (_configWatcher != null)
        {
            _configWatcher.Changed -= OnConfigChanged;
            _configWatcher.Dispose();
            _configWatcher = null;
        }
        await _controller.ShutdownAsync(cancellationToken).ConfigureAwait(false);
        await _configGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        _configGate.Release();
        ConfigChanged = null;
    }

    public void Dispose() => ShutdownAsync().GetAwaiter().GetResult();
}
