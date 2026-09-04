using System.Windows.Forms;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Matraca;

internal sealed class TrayApp : ApplicationContext
{
    private Config _config;
    private IKeyboardHook _keyboard;
    private readonly IAudioCapture _audio;
    private readonly ITargetWindow _targets;
    private readonly WindowsShell _shell;
    private readonly NotifyIcon _tray;
    private readonly DictationController _controller;
    private readonly SynchronizationContext _ui;
    private readonly SemaphoreSlim _configGate = new(1, 1);
    private readonly object _webTargetGate = new();
    private readonly string _runtimeGpu;
    private readonly WindowsWebBridge _webBridge;
    private readonly WindowsWebWindow _webWindow;
    private readonly Icon _idleIcon = LoadIcon("app.ico", SystemIcons.Application);
    private readonly Icon _recordingIcon = LoadIcon("rec.ico", SystemIcons.Exclamation);
    private readonly Icon _busyIcon = LoadIcon("busy.ico", SystemIcons.Information);
    private TargetToken? _webTarget;
    private int _exiting;

    public TrayApp()
    {
        _config = WindowsConfig.Load();
        _runtimeGpu = _config.Gpu;
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        Program.ApplyRuntimePreference(_config.Gpu);

        _tray = new NotifyIcon
        {
            Icon = _idleIcon,
            Visible = true,
            Text = "Matraca — iniciando...",
        };
        _shell = new WindowsShell(_tray, _idleIcon, _recordingIcon, _busyIcon);
        var windowsTargets = new WindowsTargetWindow();
        _targets = windowsTargets;
        var textSink = new WindowsTextSink(windowsTargets);
        _audio = new WindowsAudioCapture(_shell);
        _keyboard = BuildKeyboard(_config);
        var models = new TranscriptionModelManager(_config);
        _controller = new DictationController(
            _config,
            _keyboard,
            _audio,
            textSink,
            _targets,
            _shell,
            models,
            TextPostProcessor.TryCreate,
            config => config.History
                ? new DictationHistory(WindowsConfig.Paths, config.HistoryMaxItems)
                : null,
            action => _ui.Post(_ => action(), null),
            thread => thread.SetApartmentState(ApartmentState.STA));

        _webBridge = new WindowsWebBridge(this, _shell);
        _webWindow = new WindowsWebWindow(_webBridge);

        _tray.ContextMenuStrip = BuildMenu();
        _controller.Start();
        if (_config.ModelPath.Length == 0 || !File.Exists(_config.ModelPath))
        {
            Logger.Info($"Modelo nao encontrado ('{_config.ModelPath}'); abrindo o primeiro uso compartilhado.");
            _ui.Post(_ => _webWindow.Open("onboarding"), null);
        }
        if (_config.DiscoverMode)
        {
            _shell.ShowNotification("Modo descoberta",
                "Aperte sua tecla custom. O codigo aparece aqui e no matraca.log. " +
                "Depois coloque-o em appsettings.json (campo \"hotkey\").");
            Logger.Info("Iniciado em MODO DESCOBERTA de tecla.");
        }
    }

    private static IKeyboardHook BuildKeyboard(Config config)
        => new WindowsKeyboardHook(config);

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Configurações...", null, (_, _) => OpenWebWindow("settings"));
        menu.Items.Add("Histórico de ditados...", null, (_, _) => OpenWebWindow("history"));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Abrir matraca.log", null, (_, _) =>
        {
            try { System.Diagnostics.Process.Start("notepad.exe", WindowsConfig.Paths.LogFile); }
            catch { }
        });
        menu.Items.Add("Abrir pasta de config", null, (_, _) =>
        {
            try
            {
                Directory.CreateDirectory(WindowsConfig.Paths.DataDirectory);
                System.Diagnostics.Process.Start("explorer.exe", WindowsConfig.Paths.DataDirectory);
            }
            catch (Exception exception)
            {
                Logger.Warn("Falha ao abrir a pasta de config: " + exception.Message);
            }
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Sair", null, async (_, _) => await ExitAppAsync());
        return menu;
    }

    private void OpenWebWindow(string route)
    {
        if (_controller.IsSessionActive || _controller.IsBusy)
        {
            _shell.ShowNotification("Aguarde", "Encerre o ditado antes de abrir o painel.");
            return;
        }
        _webWindow.Open(route);
    }

    private void OpenHistory()
    {
        var history = _controller.CurrentHistory;
        if (history == null) return;
        var returnTo = _targets.CaptureActive();
        using var form = new HistoryForm(history, returnTo, _targets, _controller.Delivery);
        form.ShowDialog();
    }

    private async Task OpenSettingsAsync()
    {
        if (_controller.IsSessionActive || _controller.IsBusy)
        {
            _shell.ShowNotification("Aguarde", "Encerre a gravação antes de abrir as configurações.");
            return;
        }

        using var form = new SettingsForm(_keyboard, _audio, _shell);
        if (form.ShowDialog() != DialogResult.OK) return;
        await ApplyConfigAsync();
    }

    private async Task ApplyConfigAsync()
    {
        Config next;
        try { next = WindowsConfig.Load(); }
        catch (Exception exception)
        {
            Logger.Error("Falha ao recarregar a config", exception);
            _shell.ShowNotification("Erro", "Não consegui recarregar as configurações. Veja matraca.log.",
                ShellNotificationLevel.Error);
            return;
        }

        Config previous = _config;
        await _configGate.WaitAsync();
        try
        {
            await ApplyConfigLockedAsync(next);
        }
        catch (Exception exception)
        {
            Logger.Error("Configuracao rejeitada; estado anterior preservado", exception);
            _shell.ShowNotification(
                "Configuracao rejeitada",
                "O novo modelo nao carregou; mantive a configuracao anterior. Veja matraca.log.",
                ShellNotificationLevel.Error);
            return;
        }
        finally { _configGate.Release(); }

        if (next.Gpu != previous.Gpu)
        {
            var result = MessageBox.Show(
                "A troca entre GPU e CPU só vale reiniciando o Matraca.\n\n" +
                "Todo o resto já foi aplicado. Reiniciar agora?",
                "Matraca", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (result == DialogResult.Yes)
            {
                await RestartAppAsync();
                return;
            }
        }

        _shell.ShowNotification("Configurações aplicadas",
            $"Atalho: {next.HotkeyName} · modo: {next.Mode}.");
    }

    internal event Action<ShellState, string>? StateChanged
    {
        add => _shell.StateChanged += value;
        remove => _shell.StateChanged -= value;
    }

    internal event Action<Config>? ConfigChanged;
    internal Config CurrentConfig => _config;
    internal string RuntimeGpu => _runtimeGpu;
    internal ShellState CurrentState => _shell.CurrentState;
    internal string CurrentStateText => _shell.CurrentText;
    internal RawConfig LoadRawConfig() => WindowsConfig.LoadRaw();
    internal IReadOnlyList<string> ListAudioDevices() => _audio.ListDevices();
    internal List<DictationHistoryEntry> HistorySnapshot()
        => _controller.CurrentHistory?.Snapshot() ?? [];
    internal bool PostProcessingActive => _controller.PostProcessingActive;
    internal bool RemoveHistory(DateTime at, string text)
        => _controller.CurrentHistory?.Remove(at, text) == true;

    internal async Task<(RawConfig Config, bool RestartRequired)> ApplyAndSaveConfigPatchAsync(
        string patchJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(patchJson);
        await _configGate.WaitAsync();
        try
        {
            if (Volatile.Read(ref _exiting) != 0)
                throw new InvalidOperationException("O Matraca está encerrando.");
            RawConfig current = WindowsConfig.LoadRaw();
            JsonObject merged = JsonSerializer.SerializeToNode(current)?.AsObject()
                ?? throw new JsonException("A configuração atual não pode ser serializada.");
            JsonObject patch = JsonNode.Parse(patchJson)?.AsObject()
                ?? throw new JsonException("config.set exige um patch JSON.");
            foreach ((string name, JsonNode? value) in patch)
            {
                if (name == "postProcessApiKeyConfigured") continue;
                merged[name] = value?.DeepClone();
            }
            RawConfig raw = merged.Deserialize<RawConfig>()
                ?? throw new JsonException("O patch produziu uma configuração vazia.");
            Config previous = _config;
            Config next = Config.FromRaw(
                raw,
                WindowsHotkeyTranslator.ParseCompatibility,
                Logger.Warn,
                WindowsConfig.Paths);
            await ApplyConfigLockedAsync(next);
            try { WindowsConfig.SaveRaw(raw); }
            catch
            {
                await ApplyConfigLockedAsync(previous);
                throw;
            }
            ConfigChanged?.Invoke(next);
            bool restartRequired = next.Gpu != _runtimeGpu;
            if (restartRequired && next.Gpu != previous.Gpu)
                _ui.Post(async _ =>
                {
                    DialogResult result = MessageBox.Show(
                        "A troca entre GPU e CPU só vale reiniciando o Matraca.\n\n"
                            + "Todo o resto já foi aplicado. Reiniciar agora?",
                        "Matraca",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question);
                    if (result == DialogResult.Yes) await RestartAppAsync();
                }, null);
            return (raw, restartRequired);
        }
        catch (Exception exception)
        {
            _ui.Post(_ => _shell.ShowNotification(
                "Configuração rejeitada",
                exception.Message,
                ShellNotificationLevel.Error), null);
            throw;
        }
        finally { _configGate.Release(); }
    }

    internal bool CopyText(string text)
    {
        bool copied = false;
        Exception? failure = null;
        _ui.Send(_ =>
        {
            try
            {
                Clipboard.SetText(text);
                copied = true;
            }
            catch (Exception exception) { failure = exception; }
        }, null);
        if (failure != null) Logger.Warn("Falha ao copiar do histórico: " + failure.Message);
        return copied;
    }

    internal void CaptureWebTarget(IntPtr excludedWindow)
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
                target));
        }
        finally { _targets.Release(target); }
    }

    internal void ShowWebError(string message)
        => _ui.Post(_ => _shell.ShowNotification(
            "Operação da interface falhou",
            message,
            ShellNotificationLevel.Error), null);

    internal async Task BeginMicrophoneMonitorAsync()
    {
        if (_controller.IsSessionActive || _controller.IsBusy)
            throw new InvalidOperationException("Encerre o ditado antes de calibrar o microfone.");
        await _controller.SuspendAsync();
    }

    internal Task EndMicrophoneMonitorAsync() => _controller.ResumeAsync();

    private async Task ApplyConfigLockedAsync(Config next)
    {
        Config previous = _config;
        Config applicable = next.Gpu == _runtimeGpu ? next : next.WithGpu(_runtimeGpu);
        bool keyboardChanged = next.Hotkey != previous.Hotkey
            || next.PinHotkey != previous.PinHotkey
            || next.DiscoverMode != previous.DiscoverMode;
        if (keyboardChanged)
        {
            IKeyboardHook keyboard = BuildKeyboard(applicable);
            try
            {
                await _controller.ApplyConfigAsync(applicable, keyboard);
                _keyboard = keyboard;
            }
            catch
            {
                Config restoreConfig = previous.Gpu == _runtimeGpu
                    ? previous
                    : previous.WithGpu(_runtimeGpu);
                IKeyboardHook fallback = BuildKeyboard(restoreConfig);
                await _controller.ApplyConfigAsync(restoreConfig, fallback);
                _keyboard = fallback;
                throw;
            }
        }
        else
        {
            await _controller.ApplyConfigAsync(applicable);
        }
        _config = next;
    }

    private void RebuildMenu()
    {
        var previous = _tray.ContextMenuStrip;
        _tray.ContextMenuStrip = BuildMenu();
        try { previous?.Dispose(); } catch { }
    }

    private async Task RestartAppAsync()
    {
        Logger.Info("Reiniciando para aplicar configuracoes...");
        await ShutdownAsync();
        Application.Restart();
        ExitThread();
    }

    private async Task ExitAppAsync()
    {
        await ShutdownAsync();
        ExitThread();
    }

    private async Task ShutdownAsync()
    {
        if (Interlocked.Exchange(ref _exiting, 1) != 0) return;
        _webWindow.Shutdown();
        try { await _controller.ShutdownAsync(); }
        catch (Exception exception) { Logger.Error("Falha ao encerrar", exception); }
        try { await _webBridge.ShutdownAsync(); }
        catch (Exception exception) { Logger.Error("Falha ao encerrar interface WebView2", exception); }
        _webBridge.Dispose();
        _idleIcon.Dispose();
        _recordingIcon.Dispose();
        _busyIcon.Dispose();
        ConfigChanged = null;
    }

    private static Icon LoadIcon(string file, Icon fallback)
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, file);
            if (File.Exists(path)) return new Icon(path);
        }
        catch (Exception exception)
        {
            Logger.Warn($"Falha ao carregar icone {file}: {exception.Message}");
        }
        return fallback;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && Volatile.Read(ref _exiting) == 0)
        {
            try { ShutdownAsync().GetAwaiter().GetResult(); } catch { }
        }
        base.Dispose(disposing);
    }
}
