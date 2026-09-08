using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Matraca;

internal sealed class WindowsApplication : IWindowsWebBridgeApp, IDisposable
{
    private const uint SettingsCommand = 1;
    private const uint HistoryCommand = 2;
    private const uint LogCommand = 3;
    private const uint ConfigFolderCommand = 4;
    private const uint ExitCommand = 5;

    private static readonly TimeSpan ShutdownGracePeriod = TimeSpan.FromSeconds(5);

    private readonly WindowsDispatcher _dispatcher;
    private readonly WindowsMessageLoop _messageLoop = new();
    private readonly WindowsIconSet _icons;
    private readonly WindowsTrayIcon _tray;
    private readonly WindowsNativeShell _shell;
    private readonly WindowsTargetWindow _targets;
    private readonly WindowsAudioCapture _audio;
    private readonly DictationController _controller;
    private readonly WindowsWebBridge _webBridge;
    private readonly WindowsWebViewWindow _webWindow;
    private readonly WindowsHudWindow _hud;
    private readonly WindowsPowerMonitor _powerMonitor;
    private readonly BoundedShutdownCoordinator _shutdown;
    private readonly SemaphoreSlim _configGate = new(1, 1);
    private readonly object _webTargetGate = new();
    private readonly string _runtimeGpu;
    private Config _config;
    private WindowsUiMessages _messages;
    private WindowsKeyboardHook _keyboard;
    private WindowsConfigWatcher? _configWatcher;
    private TargetToken? _webTarget;
    private int _powerGeneration;
    private int _sleeping;
    private int _started;
    private int _stopping;
    private int _shutdownStarted;
    private int _finished;
    private int _restartRequested;
    private int _restartPromptPending;
    private int _trayMenuOpen;
    private int _microphoneMonitoring;

    public WindowsApplication()
    {
        _config = WindowsConfig.Load();
        _messages = new WindowsUiMessages(_config.EffectiveUiLanguage);
        _runtimeGpu = _config.Gpu;
        Program.ApplyRuntimePreference(_runtimeGpu);

        _dispatcher = new WindowsDispatcher();
        _icons = new WindowsIconSet();
        _tray = new WindowsTrayIcon(
            _icons,
            _messages.TrayStarting);
        _shell = new WindowsNativeShell(_tray);
        _targets = new WindowsTargetWindow();
        _audio = new WindowsAudioCapture(_shell);
        _keyboard = BuildKeyboard(_config);
        _controller = new DictationController(
            _config,
            _keyboard,
            _audio,
            new WindowsTextSink(_targets),
            _targets,
            _shell,
            new TranscriptionModelManager(_config),
            TextPostProcessor.TryCreate,
            config => config.History
                ? new DictationHistory(WindowsConfig.Paths, config.HistoryMaxItems)
                : null,
            _dispatcher.Post,
            thread => thread.SetApartmentState(ApartmentState.STA),
            new AiUsageLedger(WindowsConfig.Paths));

        _webBridge = new WindowsWebBridge(this, _shell);
        _webWindow = new WindowsWebViewWindow(_webBridge, _icons, _config);
        _tray.AppearanceChanged += () => _webWindow.ApplyAppearance(_config);
        _hud = new WindowsHudWindow(
            Path.Combine(AppContext.BaseDirectory, "Web"),
            _config.EffectiveUiLanguage);
        _powerMonitor = new WindowsPowerMonitor(_dispatcher);
        _shutdown = new BoundedShutdownCoordinator(ShutdownResourcesAsync, ShutdownGracePeriod);

        ConfigureTray();
        _shell.StateChanged += OnShellStateChanged;
        _controller.DeliveryStarted += OnDeliveryStarted;
        _controller.DeliveryCompleted += OnDeliveryCompleted;
        _powerMonitor.Suspending += OnSuspending;
        _powerMonitor.Resumed += OnResumed;
        _powerMonitor.SessionEnding += OnSessionEnding;
    }

    public event Action<ShellState, string>? StateChanged
    {
        add => _shell.StateChanged += value;
        remove => _shell.StateChanged -= value;
    }

    public event Action<Config>? ConfigChanged;

    public event Action<string, TextDeliveryResult, bool>? DeliveryCompleted
    {
        add => _controller.DeliveryCompleted += value;
        remove => _controller.DeliveryCompleted -= value;
    }

    public Config CurrentConfig => _config;

    public string RuntimeGpu => _runtimeGpu;

    public ShellState CurrentState => _shell.CurrentState;

    public string CurrentStateText => _shell.CurrentText;

    public bool PostProcessingActive => _controller.PostProcessingActive;

    public TargetToken? CurrentWebTarget
    {
        get { lock (_webTargetGate) return _webTarget; }
    }

    public int Run()
    {
        if (!_dispatcher.IsDispatchThread)
            throw new InvalidOperationException("A aplicacao Windows deve rodar na thread que criou o dispatcher.");
        if (Interlocked.Exchange(ref _started, 1) != 0)
            throw new InvalidOperationException("A aplicacao Windows ja foi iniciada.");

        _tray.Show();
        _controller.Start();
        _configWatcher = new WindowsConfigWatcher();
        _configWatcher.Changed += OnWatchedConfigChanged;

        if (_config.ModelPath.Length == 0 || !File.Exists(_config.ModelPath))
        {
            Logger.Info($"Modelo nao encontrado ('{_config.ModelPath}'); abrindo o primeiro uso compartilhado.");
            _webWindow.Open("onboarding");
        }
        if (_config.DiscoverMode)
        {
            _shell.ShowNotification(
                _messages.DiscoveryTitle,
                _messages.DiscoveryMessage);
            Logger.Info("Iniciado em MODO DESCOBERTA de tecla.");
        }

        return _messageLoop.Run();
    }

    public RawConfig LoadRawConfig() => WindowsConfig.LoadRaw();

    public IReadOnlyList<string> ListAudioDevices() => _audio.ListDevices();

    public List<DictationHistoryEntry> HistorySnapshot()
        => _controller.CurrentHistory?.Snapshot() ?? [];

    public List<AiUsageBucket> AiUsageSnapshot()
        => _controller.UsageLedger?.Snapshot() ?? [];

    public bool RemoveHistory(DateTime at, string text)
        => _controller.CurrentHistory?.Remove(at, text) == true;

    public void ClearHistory() => _controller.CurrentHistory?.Clear();

    public Task<string?> PickFileAsync(string kind)
        => OnDispatcherAsync(() => kind switch
        {
            "model" => WindowsFilePicker.PickModel(_webWindow.Handle, _config.EffectiveUiLanguage),
            "sound" => WindowsFilePicker.PickSound(_webWindow.Handle, _config.EffectiveUiLanguage),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        });

    public Task<int> PreviewSoundAsync(bool start, string? filePath)
        => OnDispatcherAsync(() => _shell.PlaySound(start, filePath, _config.BeepVolume));

    public async Task<(RawConfig Config, bool RestartRequired)> ApplyAndSaveConfigPatchAsync(
        string patchJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(patchJson);
        await _configGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _stopping) != 0)
                throw new InvalidOperationException("O Matraca esta encerrando.");

            RawConfig current = WindowsConfig.LoadRaw();
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
            Config previous = _config;
            Config next = Config.FromRaw(
                raw,
                WindowsHotkeyTranslator.ParseCompatibility,
                Logger.Warn,
                WindowsConfig.Paths);
            await ApplyConfigLockedAsync(next).ConfigureAwait(false);
            try
            {
                WindowsConfig.SaveRaw(raw);
            }
            catch
            {
                await ApplyConfigLockedAsync(previous).ConfigureAwait(false);
                throw;
            }

            ConfigChanged?.Invoke(next);
            bool restartRequired = next.Gpu != _runtimeGpu;
            if (restartRequired && next.Gpu != previous.Gpu) PromptForRestart();
            else if (!restartRequired) CancelRestartPrompt();
            return (raw, restartRequired);
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

    public bool CopyText(string text)
    {
        if (_dispatcher.IsDispatchThread) return WindowsClipboard.TryWriteText(text);

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            _dispatcher.Post(() =>
            {
                try { completion.SetResult(WindowsClipboard.TryWriteText(text)); }
                catch (Exception exception) { completion.SetException(exception); }
            });
            return completion.Task.GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            Logger.Warn("Falha ao copiar do historico: " + exception.Message);
            return false;
        }
    }

    public void CaptureWebTarget(nint excludedWindow)
    {
        if (TextInjector.GetForegroundWindowHandle() == excludedWindow) return;
        TargetToken? next = _targets.CaptureActive();
        TargetToken? previous;
        lock (_webTargetGate)
        {
            previous = _webTarget;
            _webTarget = next;
        }
        if (previous != null) _targets.Release(previous);
    }

    public void ReleaseWebTarget()
    {
        TargetToken? target;
        lock (_webTargetGate)
        {
            target = _webTarget;
            _webTarget = null;
        }
        if (target != null) _targets.Release(target);
    }

    public TargetToken? TakeWebTarget()
    {
        lock (_webTargetGate)
        {
            TargetToken? target = _webTarget;
            _webTarget = null;
            return target;
        }
    }

    public async Task<TextDeliveryResult> RepasteAsync(string text, TargetToken target)
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

    public Task ToggleDictationFromUiAsync(TargetToken? target)
        => _controller.ToggleDictationFromUiAsync(target);

    public void ShowWebError(string message)
        => PostToDispatcher(() => _shell.ShowNotification(
            _messages.InterfaceErrorTitle,
            message,
            ShellNotificationLevel.Error));

    public async Task BeginMicrophoneMonitorAsync()
    {
        if (_controller.IsSessionActive || _controller.IsBusy)
            throw new InvalidOperationException("Encerre o ditado antes de calibrar o microfone.");
        if (Interlocked.Exchange(ref _microphoneMonitoring, 1) != 0) return;
        try { await _controller.SuspendAsync().ConfigureAwait(false); }
        catch
        {
            Volatile.Write(ref _microphoneMonitoring, 0);
            throw;
        }
    }

    public Task EndMicrophoneMonitorAsync()
    {
        Volatile.Write(ref _microphoneMonitoring, 0);
        return ResumeControllerIfAvailableAsync();
    }

    private static WindowsKeyboardHook BuildKeyboard(Config config)
        => new WindowsKeyboardHook(config);

    private void ConfigureTray()
    {
        _tray.AddMenuItem(SettingsCommand, _messages.SettingsMenu, () => OpenWebWindow("settings"));
        _tray.AddMenuItem(HistoryCommand, _messages.HistoryMenu, () => OpenWebWindow("history"));
        _tray.AddMenuSeparator();
        _tray.AddMenuItem(LogCommand, _messages.LogMenu, OpenLog);
        _tray.AddMenuItem(ConfigFolderCommand, _messages.ConfigFolderMenu, OpenConfigFolder);
        _tray.AddMenuSeparator();
        _tray.AddMenuItem(ExitCommand, _messages.ExitMenu, () => RequestShutdown(restart: false));
        _tray.Activated += () => OpenWebWindow("home");
        _tray.MenuOpening = OnTrayMenuOpening;
        _tray.MenuClosed += OnTrayMenuClosed;
    }

    private void OpenWebWindow(string route)
    {
        if (Volatile.Read(ref _stopping) != 0) return;
        if (_controller.IsSessionActive || _controller.IsBusy)
        {
            _shell.ShowNotification(_messages.WaitTitle, _messages.WaitMessage);
            return;
        }
        _webWindow.Open(route);
    }

    private static void OpenLog()
    {
        try
        {
            var start = new ProcessStartInfo("notepad.exe") { UseShellExecute = true };
            start.ArgumentList.Add(WindowsConfig.Paths.LogFile);
            Process.Start(start);
        }
        catch (Exception exception)
        {
            Logger.Warn("Falha ao abrir matraca.log: " + exception.Message);
        }
    }

    private static void OpenConfigFolder()
    {
        try
        {
            Directory.CreateDirectory(WindowsConfig.Paths.DataDirectory);
            Process.Start(new ProcessStartInfo(WindowsConfig.Paths.DataDirectory)
            {
                UseShellExecute = true,
            });
        }
        catch (Exception exception)
        {
            Logger.Warn("Falha ao abrir a pasta de config: " + exception.Message);
        }
    }

    private void OnShellStateChanged(ShellState state, string text)
    {
        if (state != ShellState.Idle) _hud.SetTargetWindow(ResolveHudTarget());
        _hud.PublishShellState(state, text, _config.Mode);
        if (state == ShellState.Idle && Volatile.Read(ref _restartPromptPending) != 0)
            PostToDispatcher(TryShowRestartPrompt);
    }

    private bool OnTrayMenuOpening()
    {
        if (_controller.IsSessionActive || _controller.IsBusy)
        {
            _shell.ShowNotification(_messages.WaitTitle, _messages.MenuWaitMessage);
            return false;
        }

        Volatile.Write(ref _trayMenuOpen, 1);
        _keyboard.InputSuppressed = true;
        try
        {
            _controller.SuspendAsync().GetAwaiter().GetResult();
            return true;
        }
        catch (Exception exception)
        {
            Volatile.Write(ref _trayMenuOpen, 0);
            _keyboard.InputSuppressed = false;
            try { ResumeControllerIfAvailableAsync().GetAwaiter().GetResult(); }
            catch { }
            Logger.Error("Falha ao suspender ditado antes de abrir o menu", exception);
            _shell.ShowNotification(_messages.MenuUnavailableTitle, _messages.MenuUnavailableMessage);
            return false;
        }
    }

    private void OnTrayMenuClosed()
    {
        Volatile.Write(ref _trayMenuOpen, 0);
        _keyboard.InputSuppressed = false;
        try { ResumeControllerIfAvailableAsync().GetAwaiter().GetResult(); }
        catch (Exception exception) { Logger.Error("Falha ao retomar ditado depois do menu", exception); }
    }

    private nint ResolveHudTarget()
    {
        TargetToken? pinned = _controller.PinnedTarget;
        if (pinned != null && _targets.TryResolve(pinned, out nint target)) return target;
        return WindowsNativeMethods.GetForegroundWindow();
    }

    private void OnDeliveryStarted(bool streaming)
        => _hud.PublishDeliveryStarted(streaming, _config.Mode);

    private void OnDeliveryCompleted(string text, TextDeliveryResult result, bool streaming)
        => _hud.PublishDeliveryResult(text, result, streaming);

    private void OnWatchedConfigChanged(Config config)
    {
        if (Volatile.Read(ref _stopping) == 0)
            PostToDispatcher(() => Observe(ApplyConfigAsync(config)));
    }

    private async Task ApplyConfigAsync(Config config)
    {
        await _configGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _stopping) != 0) return;
            Config previous = _config;
            await ApplyConfigLockedAsync(config).ConfigureAwait(false);
            ConfigChanged?.Invoke(config);
            if (config.Gpu != _runtimeGpu && config.Gpu != previous.Gpu) PromptForRestart();
            else if (config.Gpu == _runtimeGpu) CancelRestartPrompt();
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

    private async Task ApplyConfigLockedAsync(Config next)
    {
        Config previous = _config;
        Config applicable = next.Gpu == _runtimeGpu ? next : next.WithGpu(_runtimeGpu);
        bool keyboardChanged = next.Hotkey != previous.Hotkey
            || next.PinHotkey != previous.PinHotkey
            || next.DiscoverMode != previous.DiscoverMode;
        if (keyboardChanged)
        {
            WindowsKeyboardHook keyboard = BuildKeyboard(applicable);
            keyboard.InputSuppressed = Volatile.Read(ref _trayMenuOpen) != 0;
            try
            {
                await _controller.ApplyConfigAsync(applicable, keyboard).ConfigureAwait(false);
                _keyboard = keyboard;
                _keyboard.InputSuppressed = Volatile.Read(ref _trayMenuOpen) != 0;
            }
            catch
            {
                Config restoreConfig = previous.Gpu == _runtimeGpu
                    ? previous
                    : previous.WithGpu(_runtimeGpu);
                WindowsKeyboardHook fallback = BuildKeyboard(restoreConfig);
                fallback.InputSuppressed = Volatile.Read(ref _trayMenuOpen) != 0;
                await _controller.ApplyConfigAsync(restoreConfig, fallback).ConfigureAwait(false);
                _keyboard = fallback;
                _keyboard.InputSuppressed = Volatile.Read(ref _trayMenuOpen) != 0;
                throw;
            }
        }
        else
        {
            await _controller.ApplyConfigAsync(applicable).ConfigureAwait(false);
        }
        _config = next;
        await OnDispatcherAsync(() =>
        {
            ApplyNativeLocalization(next);
            _webWindow.ApplyAppearance(next);
            return true;
        }).ConfigureAwait(false);
    }

    private void PromptForRestart()
    {
        Volatile.Write(ref _restartPromptPending, 1);
        PostToDispatcher(TryShowRestartPrompt);
    }

    private void CancelRestartPrompt() => Volatile.Write(ref _restartPromptPending, 0);

    private void TryShowRestartPrompt()
    {
        if (Volatile.Read(ref _stopping) != 0
            || Volatile.Read(ref _restartPromptPending) == 0
            || _controller.IsSessionActive
            || _controller.IsBusy)
            return;

        Volatile.Write(ref _restartPromptPending, 0);
        int result = WindowsNativeMethods.MessageBox(
            _dispatcher.WindowHandle,
            _messages.RestartPrompt,
            "Matraca",
            WindowsNativeMethods.MbYesNo | WindowsNativeMethods.MbIconQuestion);
        if (result == WindowsNativeMethods.IdYes) RequestShutdown(restart: true);
    }

    private void NotifyConfigRejected(Exception exception)
        => PostToDispatcher(() => _shell.ShowNotification(
            _messages.ConfigRejectedTitle,
            _messages.ConfigError(exception),
            ShellNotificationLevel.Error));

    private void ApplyNativeLocalization(Config config)
    {
        _messages = new WindowsUiMessages(config.EffectiveUiLanguage);
        _tray.UpdateMenuItem(SettingsCommand, _messages.SettingsMenu);
        _tray.UpdateMenuItem(HistoryCommand, _messages.HistoryMenu);
        _tray.UpdateMenuItem(LogCommand, _messages.LogMenu);
        _tray.UpdateMenuItem(ConfigFolderCommand, _messages.ConfigFolderMenu);
        _tray.UpdateMenuItem(ExitCommand, _messages.ExitMenu);
        _hud.ApplyConfig(config);

        UiMessageCatalog coreMessages = new(config.EffectiveUiLanguage);
        string status = _shell.CurrentState switch
        {
            ShellState.Recording => coreMessages.Recording(config.HotkeyNeedsKeyUp),
            ShellState.Busy => coreMessages.Transcribing,
            ShellState.Writing => coreMessages.Writing,
            ShellState.Error => coreMessages.ModelLoadErrorState,
            _ => config.DiscoverMode ? coreMessages.DiscoveryMode : coreMessages.Ready(config.HotkeyName),
        };
        _shell.SetState(_shell.CurrentState, status);
    }

    private void OnSuspending()
    {
        if (Volatile.Read(ref _stopping) != 0
            || Interlocked.Exchange(ref _sleeping, 1) != 0)
            return;

        Interlocked.Increment(ref _powerGeneration);
        Logger.Info("Windows vai entrar em suspensao.");
        try { _controller.SuspendAsync().GetAwaiter().GetResult(); }
        catch (Exception exception) { Logger.Error("Falha ao suspender pipeline antes do repouso", exception); }
    }

    private void OnResumed()
    {
        if (Volatile.Read(ref _stopping) != 0
            || Interlocked.CompareExchange(ref _sleeping, 0, 1) != 1)
            return;

        int generation = Volatile.Read(ref _powerGeneration);
        Logger.Info("Windows retomou da suspensao.");
        Observe(ResumeAsync(generation));
    }

    private async Task ResumeAsync(int generation)
    {
        try
        {
            await ApplyConfigAsync(WindowsConfig.Load()).ConfigureAwait(false);
        }
        catch
        {
            // ApplyConfigAsync already reported the rejected configuration.
        }

        if (Volatile.Read(ref _stopping) != 0
            || Volatile.Read(ref _sleeping) != 0
            || Volatile.Read(ref _powerGeneration) != generation)
            return;

        PostToDispatcher(() =>
        {
            if (Volatile.Read(ref _stopping) == 0
                && Volatile.Read(ref _sleeping) == 0
                && Volatile.Read(ref _powerGeneration) == generation)
                Observe(ResumeControllerIfAvailableAsync());
        });
    }

    private async Task ResumeControllerIfAvailableAsync()
    {
        if (!CanResumeController()) return;
        await _controller.ResumeAsync().ConfigureAwait(false);
        if (!CanResumeController()) await _controller.SuspendAsync().ConfigureAwait(false);
    }

    private bool CanResumeController()
        => Volatile.Read(ref _stopping) == 0
            && Volatile.Read(ref _sleeping) == 0
            && Volatile.Read(ref _trayMenuOpen) == 0
            && Volatile.Read(ref _microphoneMonitoring) == 0;

    private void OnSessionEnding()
    {
        RequestShutdown(restart: false);
        try
        {
            bool completed = _shutdown.RequestShutdownAsync().GetAwaiter().GetResult();
            if (!completed)
                Logger.Warn("Encerramento do pipeline excedeu o prazo antes do fim da sessao.");
        }
        catch (Exception exception)
        {
            Logger.Error("Falha ao encerrar pipeline antes do fim da sessao", exception);
        }
    }

    private void RequestShutdown(bool restart)
    {
        if (restart) Volatile.Write(ref _restartRequested, 1);
        if (Interlocked.Exchange(ref _stopping, 1) != 0) return;

        if (_dispatcher.IsDispatchThread)
            BeginShutdownOnOwnerThread();
        else
            PostToDispatcher(BeginShutdownOnOwnerThread);
    }

    private void BeginShutdownOnOwnerThread()
    {
        if (Interlocked.Exchange(ref _shutdownStarted, 1) != 0) return;

        _configWatcher?.Dispose();
        _configWatcher = null;
        _powerMonitor.Dispose();
        _webWindow.Hide();
        Observe(FinishWhenShutdownCompletesAsync());
    }

    private async Task FinishWhenShutdownCompletesAsync()
    {
        try
        {
            bool completed = await _shutdown.RequestShutdownAsync().ConfigureAwait(false);
            if (!completed)
                Logger.Warn(
                    $"Encerramento excedeu a graca de {ShutdownGracePeriod.TotalSeconds:0}s; "
                    + "finalizando o casco nativo.");
        }
        catch (Exception exception)
        {
            Logger.Error("Falha durante o encerramento nativo", exception);
        }
        finally
        {
            PostToDispatcher(FinishShutdownOnOwnerThread);
        }
    }

    private async Task ShutdownResourcesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _webBridge.ShutdownAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Logger.Warn("Cancelamento da interface excedeu o prazo de encerramento.");
        }
        catch (Exception exception)
        {
            Logger.Error("Falha ao encerrar interface WebView2", exception);
        }

        ReleaseWebTarget();

        try
        {
            await _controller.ShutdownAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Logger.Warn("Pipeline de ditado excedeu o prazo de encerramento.");
        }
        catch (Exception exception)
        {
            Logger.Error("Falha ao encerrar pipeline de ditado", exception);
        }

        try
        {
            await _configGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            _configGate.Release();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void FinishShutdownOnOwnerThread()
    {
        if (Interlocked.Exchange(ref _finished, 1) != 0) return;

        _controller.DeliveryStarted -= OnDeliveryStarted;
        _controller.DeliveryCompleted -= OnDeliveryCompleted;
        _shell.StateChanged -= OnShellStateChanged;
        _tray.MenuOpening = null;
        _tray.MenuClosed -= OnTrayMenuClosed;
        TryDispose(_webWindow.Shutdown, "janela WebView2");
        TryDispose(_hud.Shutdown, "HUD");
        TryDispose(_targets.Dispose, "moldura de foco");
        TryDispose(_webBridge.Dispose, "bridge WebView2");
        TryDispose(_shell.Dispose, "shell nativo");
        TryDispose(_tray.Dispose, "icone da bandeja");
        TryDispose(_icons.Dispose, "icones nativos");

        ConfigChanged = null;

        if (Volatile.Read(ref _restartRequested) != 0) StartReplacementProcess();

        TryDispose(_dispatcher.Dispose, "dispatcher Win32");
        _messageLoop.Exit();
    }

    private static void StartReplacementProcess()
    {
        try
        {
            string executable = Environment.ProcessPath
                ?? throw new InvalidOperationException("O caminho do executavel atual nao esta disponivel.");
            Process.Start(new ProcessStartInfo(executable)
            {
                UseShellExecute = true,
                WorkingDirectory = AppContext.BaseDirectory,
            });
        }
        catch (Exception exception)
        {
            Logger.Error("Falha ao reiniciar o Matraca", exception);
        }
    }

    private void PostToDispatcher(Action action)
    {
        try
        {
            if (_dispatcher.IsDispatchThread) action();
            else _dispatcher.Post(action);
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private Task<T> OnDispatcherAsync<T>(Func<T> action)
    {
        if (_dispatcher.IsDispatchThread) return Task.FromResult(action());

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _dispatcher.Post(() =>
        {
            try { completion.SetResult(action()); }
            catch (Exception exception) { completion.SetException(exception); }
        });
        return completion.Task;
    }

    private static void Observe(Task task)
        => _ = task.ContinueWith(
            failed => Logger.Error(
                "Falha em operacao assincrona da aplicacao Windows",
                failed.Exception!.GetBaseException()),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);

    private static void TryDispose(Action action, string resource)
    {
        try { action(); }
        catch (Exception exception) { Logger.Error($"Falha ao encerrar {resource}", exception); }
    }

    public void Dispose()
    {
        if (Volatile.Read(ref _finished) != 0) return;
        RequestShutdown(restart: false);
    }
}
