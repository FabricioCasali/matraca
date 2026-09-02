using System.Windows.Forms;

namespace Matraca;

internal sealed class TrayApp : ApplicationContext
{
    private Config _config;
    private IKeyboardHook _keyboard;
    private readonly IAudioCapture _audio;
    private readonly ITargetWindow _targets;
    private readonly IShell _shell;
    private readonly NotifyIcon _tray;
    private readonly DictationController _controller;
    private readonly SynchronizationContext _ui;
    private readonly Icon _idleIcon = LoadIcon("app.ico", SystemIcons.Application);
    private readonly Icon _recordingIcon = LoadIcon("rec.ico", SystemIcons.Exclamation);
    private readonly Icon _busyIcon = LoadIcon("busy.ico", SystemIcons.Information);
    private int _exiting;

    public TrayApp()
    {
        _config = WindowsConfig.Load();
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

        _tray.ContextMenuStrip = BuildMenu();
        _controller.Start();
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
        menu.Items.Add("Configurações...", null, async (_, _) => await OpenSettingsAsync());
        if (_controller.CurrentHistory != null)
            menu.Items.Add("Histórico de ditados...", null, (_, _) => OpenHistory());
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
        Config previous = _config;
        Config next;
        try { next = WindowsConfig.Load(); }
        catch (Exception exception)
        {
            Logger.Error("Falha ao recarregar a config", exception);
            _shell.ShowNotification("Erro", "Não consegui recarregar as configurações. Veja matraca.log.",
                ShellNotificationLevel.Error);
            return;
        }

        if (next.Hotkey != previous.Hotkey || next.PinHotkey != previous.PinHotkey ||
            next.DiscoverMode != previous.DiscoverMode)
        {
            _keyboard = BuildKeyboard(next);
            await _controller.ReplaceKeyboardHookAsync(_keyboard);
        }

        _config = next;
        await _controller.ApplyConfigAsync(next);
        if (next.History != previous.History || next.HistoryMaxItems != previous.HistoryMaxItems)
            RebuildMenu();

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
        try { await _controller.ShutdownAsync(); }
        catch (Exception exception) { Logger.Error("Falha ao encerrar", exception); }
        _idleIcon.Dispose();
        _recordingIcon.Dispose();
        _busyIcon.Dispose();
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
