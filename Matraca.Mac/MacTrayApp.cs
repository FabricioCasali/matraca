using Matraca.Core;
using Matraca.Mac.Config;
using Matraca.Mac.Platform;
using Matraca.Mac.Platform.Audio;
using Matraca.Mac.Platform.Interop;
using Matraca.Mac.Platform.Keyboard;
using Matraca.Mac.Platform.Speech;
using Matraca.Mac.Platform.Text;
using CoreConfig = Matraca.Core.Config;

namespace Matraca.Mac;

internal sealed class MacTrayApp : IDisposable
{
    private readonly DictationController _controller;
    private readonly SemaphoreSlim _configGate = new(1, 1);
    private readonly MacShell _shell;
    private readonly string _runtimeGpu;
    private CoreConfig _config;
    private MacConfigWatcher? _configWatcher;
    private int _stopping;

    public MacTrayApp(CoreConfig config, MacStatusItem statusItem)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _runtimeGpu = config.Gpu;
        ArgumentNullException.ThrowIfNull(statusItem);

        var keyboard = new MacKeyboardHook(config);
        var audio = new MacAudioCapture();
        var targets = new MacTargetWindow();
        _shell = new MacShell(statusItem);
        _controller = new DictationController(
            config,
            keyboard,
            audio,
            new MacTextSink(targets),
            targets,
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
        _configWatcher = new MacConfigWatcher();
        _configWatcher.Changed += OnConfigChanged;
    }

    private void OnConfigChanged(CoreConfig config)
    {
        if (Volatile.Read(ref _stopping) == 0)
            MainThread.Post(() => Observe(ApplyConfigAsync(config)));
    }

    private async Task ApplyConfigAsync(CoreConfig config)
    {
        await _configGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _stopping) != 0) return;
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
            if (accelerationChanged)
                MainThread.Post(() => _shell.ShowNotification(
                    "Reinicie o Matraca",
                    "A troca entre GPU e CPU passa a valer na proxima inicializacao.",
                    ShellNotificationLevel.Warning));
        }
        catch (Exception exception)
        {
            MainThread.Post(() => _shell.ShowNotification(
                "Configuracao rejeitada",
                exception.Message,
                ShellNotificationLevel.Error));
            throw;
        }
        finally
        {
            _configGate.Release();
        }
    }

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
        if (_configWatcher != null)
        {
            _configWatcher.Changed -= OnConfigChanged;
            _configWatcher.Dispose();
            _configWatcher = null;
        }
        await _controller.ShutdownAsync(cancellationToken).ConfigureAwait(false);
        await _configGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        _configGate.Release();
    }

    public void Dispose() => ShutdownAsync().GetAwaiter().GetResult();
}
