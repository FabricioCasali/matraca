using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using Matraca.Core;

namespace Matraca;

internal sealed class WindowsHudWindow : IDisposable
{
    private const int Width = 430;
    private const int Height = 92;
    private const int BottomMargin = 34;
    private const uint DefaultDpi = 96;
    private const int HitTestTransparent = -1;
    private const int MouseActivateNoActivateAndEat = 4;

    private readonly WindowsDispatcher _dispatcher;
    private readonly WindowsNativeWindow _window;
    private readonly WindowsWebViewHost _host;
    private bool _ready;
    private bool _dictationActive;
    private bool _deliveryJustCompleted;
    private bool _deliveryError;
    private readonly WindowsConfigWatcher _appearanceWatcher;
    private WindowsUiMessages _messages;
    private string _themeMode = "system";
    private string _palette = "olive";
    private string _uiLanguage;
    private string _effectiveUiLanguage;
    private bool _visible;
    private bool _hasPendingPublication;
    private string _pendingState = "ready";
    private string _pendingTitle;
    private string _pendingDetail = string.Empty;
    private string _mode = string.Empty;
    private nint _targetWindow;
    private int _hideGeneration;
    private int _shutdownRequested;
    private int _disposed;

    public WindowsHudWindow(string authorizedAssetRoot, string effectiveUiLanguage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authorizedAssetRoot);
        _messages = new WindowsUiMessages(effectiveUiLanguage);
        _uiLanguage = effectiveUiLanguage;
        _effectiveUiLanguage = effectiveUiLanguage;
        _pendingTitle = _messages.HudReadyTitle;
        _dispatcher = new WindowsDispatcher();
        try
        {
            _window = new WindowsNativeWindow(
                "HudWindow",
                WindowProcedure,
                "Matraca HUD",
                WindowsNativeMethods.WsPopup,
                WindowsNativeMethods.WsExLayered
                    | WindowsNativeMethods.WsExTransparent
                    | WindowsNativeMethods.WsExToolWindow
                    | WindowsNativeMethods.WsExNoActivate,
                0,
                0,
                Width,
                Height);
            if (!WindowsNativeMethods.SetLayeredWindowAttributes(
                    _window.Handle,
                    0,
                    byte.MaxValue,
                    WindowsNativeMethods.LayeredWindowAlpha))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Falha ao configurar a transparencia do HUD.");

            _host = new WindowsWebViewHost(
                _window.Handle,
                authorizedAssetRoot,
                Path.Combine(WindowsConfig.Paths.DataDirectory, "WebView2"),
                DispatchHost,
                entryPath: "hud.html",
                transparentBackground: true);
        }
        catch
        {
            _window?.Dispose();
            _dispatcher.Dispose();
            throw;
        }

        _host.MessageReceived += OnMessageReceived;
        _appearanceWatcher = new WindowsConfigWatcher();
        _appearanceWatcher.Changed += OnAppearanceChanged;
        RefreshAppearance();
        StartInitialization();
    }

    public nint Handle => _window.Handle;

    private void OnAppearanceChanged(Config config) => Dispatch(() => ApplyConfigOnOwnerThread(config));

    public void ApplyConfig(Config config)
    {
        ArgumentNullException.ThrowIfNull(config);
        Dispatch(() => ApplyConfigOnOwnerThread(config));
    }

    private void ApplyConfigOnOwnerThread(Config config)
    {
        _messages = new WindowsUiMessages(config.EffectiveUiLanguage);
        _themeMode = config.ThemeMode;
        _palette = config.Palette;
        _uiLanguage = config.UiLanguage;
        _effectiveUiLanguage = config.EffectiveUiLanguage;
        PublishAppearance();
    }

    private void RefreshAppearance()
    {
        Config config = WindowsConfig.Load();
        ApplyConfigOnOwnerThread(config);
    }

    private void PublishAppearance()
    {
        if (_ready)
            _host.PostJson(JsonSerializer.Serialize(new
            {
                version = 1,
                type = "hud.appearance",
                payload = new
                {
                    themeMode = _themeMode,
                    palette = _palette,
                    uiLanguage = _uiLanguage,
                    effectiveUiLanguage = _effectiveUiLanguage,
                },
            }));
    }

    public void SetTargetWindow(nint targetWindow)
        => Dispatch(() => _targetWindow = targetWindow);

    public void PublishShellState(ShellState state, string text, string mode)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(mode);
        Dispatch(() => ApplyShellState(state, text, mode));
    }

    public void PublishDeliveryStarted(bool streaming, string mode)
    {
        ArgumentNullException.ThrowIfNull(mode);
        Dispatch(() =>
        {
            _mode = mode;
            _deliveryJustCompleted = false;
            Interlocked.Increment(ref _hideGeneration);
            if (_deliveryError) return;
            Publish("writing", streaming && _dictationActive ? _messages.HudWritingStreaming : _messages.HudWriting, mode);
        });
    }

    public void PublishDeliveryResult(string text, TextDeliveryResult result, bool streaming)
    {
        ArgumentNullException.ThrowIfNull(text);
        Dispatch(() => ApplyDeliveryResult(text, result, streaming));
    }

    public void Shutdown()
    {
        if (Interlocked.Exchange(ref _shutdownRequested, 1) != 0) return;
        if (_window.IsOwnerThread)
        {
            ShutdownOnOwnerThread();
            return;
        }

        try { _dispatcher.Post(ShutdownOnOwnerThread); }
        catch (ObjectDisposedException) { }
    }

    public void Dispose() => Shutdown();

    private void StartInitialization()
    {
        Task task;
        try { task = _host.InitializeAsync(); }
        catch (Exception exception)
        {
            Logger.Error("Falha ao iniciar WebView2 do HUD nativo", exception);
            return;
        }

        _ = task.ContinueWith(
            completed =>
            {
                if (completed.IsFaulted)
                {
                    Logger.Error("Falha ao iniciar WebView2 do HUD nativo", completed.Exception!.GetBaseException());
                    return;
                }
                if (completed.IsCanceled) return;
                Dispatch(() =>
                {
                    ResizeHost();
                    _host.SetVisible(_visible);
                });
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void OnMessageReceived(string json)
    {
        if (Volatile.Read(ref _shutdownRequested) != 0) return;
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("version", out JsonElement version)
                || version.ValueKind != JsonValueKind.Number
                || version.GetInt32() != 1
                || !root.TryGetProperty("type", out JsonElement type)
                || type.ValueKind != JsonValueKind.String
                || type.GetString() != "ui.hudReady")
                return;
        }
        catch (JsonException)
        {
            return;
        }

        _ready = true;
        RefreshAppearance();
        if (_hasPendingPublication)
            Publish(_pendingState, _pendingTitle, _pendingDetail, invalidateTimers: false);
    }

    private void ApplyShellState(ShellState state, string text, string mode)
    {
        _mode = mode;
        if (state == ShellState.Idle)
        {
            _dictationActive = false;
            if (_deliveryError) return;
            if (_deliveryJustCompleted)
            {
                _deliveryJustCompleted = false;
            }
            else
            {
                Interlocked.Increment(ref _hideGeneration);
                Hide();
            }
            return;
        }

        if (state is ShellState.Busy or ShellState.Writing) _dictationActive = false;
        if (_deliveryError && !(state == ShellState.Recording && !_dictationActive)) return;
        _deliveryError = false;
        Interlocked.Increment(ref _hideGeneration);
        if (state == ShellState.Recording) _dictationActive = true;
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
            ShellState.Recording => _messages.HudListeningTitle,
            ShellState.Busy => _messages.HudThinkingTitle,
            ShellState.Writing => _messages.HudWritingTitle,
            ShellState.Error => _messages.HudAttentionTitle,
            _ => _messages.HudReadyTitle,
        };
        Publish(name, title, state == ShellState.Recording ? mode : text);
    }

    private void ApplyDeliveryResult(string text, TextDeliveryResult result, bool streaming)
    {
        bool delivered = result == TextDeliveryResult.Delivered;
        Interlocked.Increment(ref _hideGeneration);
        _deliveryError = !delivered;
        _deliveryJustCompleted = !streaming || !_dictationActive;
        Publish(
            delivered ? "done" : "error",
            delivered
                ? (streaming && _dictationActive ? _messages.HudDoneStreaming : _messages.HudDone)
                : _messages.HudDeliveryError,
            text);
        if (!delivered) return;
        if (streaming && _dictationActive)
            ReturnToListeningLater();
        else
            HideLater();
    }

    private void Publish(string state, string title, string detail, bool invalidateTimers = true)
    {
        if (invalidateTimers) Interlocked.Increment(ref _hideGeneration);
        _pendingState = state;
        _pendingTitle = title;
        _pendingDetail = detail;
        _hasPendingPublication = true;
        if (!_ready) return;

        PositionAndShow();
        _host.PostJson(JsonSerializer.Serialize(new
        {
            version = 1,
            type = "hud.state",
            payload = new { state, title, detail, generation = _hideGeneration },
        }));
    }

    private void PositionAndShow()
    {
        nint reference = ResolveReferenceWindow();
        WindowsRectangle workArea = GetWorkArea(reference);
        uint dpi = reference != nint.Zero ? WindowsNativeMethods.GetDpiForWindow(reference) : 0;
        if (dpi == 0) dpi = WindowsNativeMethods.GetDpiForSystem();
        if (dpi == 0) dpi = DefaultDpi;
        int width = Scale(Width, dpi);
        int height = Scale(Height, dpi);
        int x = workArea.Left + Math.Max(0, (workArea.Width - width) / 2);
        int y = Math.Max(workArea.Top, workArea.Bottom - height - Scale(BottomMargin, dpi));

        _host.SetVisible(true);
        if (!WindowsNativeMethods.SetWindowPos(
                _window.Handle,
                WindowsNativeMethods.TopMostWindow,
                x,
                y,
                width,
                height,
                WindowsNativeMethods.SwpNoActivate | WindowsNativeMethods.SwpShowWindow))
        {
            _host.SetVisible(false);
            _visible = false;
            WindowsNativeMethods.ShowWindow(_window.Handle, WindowsNativeMethods.SwHide);
            Logger.Warn("Nao foi possivel posicionar o HUD nativo.");
            return;
        }

        _visible = true;
        ResizeHost();
        _host.NotifyParentWindowPositionChanged();
    }

    private nint ResolveReferenceWindow()
    {
        if (_targetWindow != nint.Zero && WindowsNativeMethods.IsWindow(_targetWindow))
            return _targetWindow;
        nint foreground = WindowsNativeMethods.GetForegroundWindow();
        return foreground != _window.Handle && WindowsNativeMethods.IsWindow(foreground)
            ? foreground
            : nint.Zero;
    }

    private static WindowsRectangle GetWorkArea(nint reference)
    {
        nint monitor = WindowsNativeMethods.MonitorFromWindow(
            reference,
            reference == nint.Zero
                ? WindowsNativeMethods.MonitorDefaultToPrimary
                : WindowsNativeMethods.MonitorDefaultToNearest);
        if (monitor != nint.Zero)
        {
            var info = new WindowsMonitorInfo { Size = (uint)Marshal.SizeOf<WindowsMonitorInfo>() };
            if (WindowsNativeMethods.GetMonitorInfo(monitor, ref info)) return info.WorkArea;
        }

        if (WindowsNativeMethods.SystemParametersInfo(
                WindowsNativeMethods.SpiGetWorkArea,
                0,
                out WindowsRectangle workArea,
                0))
            return workArea;

        return new WindowsRectangle
        {
            Right = WindowsNativeMethods.GetSystemMetrics(WindowsNativeMethods.SmCxScreen),
            Bottom = WindowsNativeMethods.GetSystemMetrics(WindowsNativeMethods.SmCyScreen),
        };
    }

    private nint WindowProcedure(nint window, uint message, nint wParam, nint lParam)
    {
        switch (message)
        {
            case WindowsNativeMethods.WmNcHitTest:
                return HitTestTransparent;
            case WindowsNativeMethods.WmMouseActivate:
                return MouseActivateNoActivateAndEat;
            case WindowsNativeMethods.WmSize:
                ResizeHost();
                return nint.Zero;
            case WindowsNativeMethods.WmMove:
                _host?.NotifyParentWindowPositionChanged();
                break;
            case WindowsNativeMethods.WmDpiChanged:
                ApplyDpiBounds(window, lParam);
                return nint.Zero;
            case WindowsNativeMethods.WmClose:
                _deliveryError = false;
                Interlocked.Increment(ref _hideGeneration);
                Hide();
                return nint.Zero;
        }

        return WindowsNativeMethods.DefWindowProcW(window, message, wParam, lParam);
    }

    private void ResizeHost()
    {
        if (_host == null || _window == null) return;
        if (WindowsNativeMethods.GetClientRect(_window.Handle, out WindowsRectangle client))
            _host.Resize(client.Width, client.Height);
    }

    private void Hide()
    {
        _hasPendingPublication = false;
        if (!_visible) return;
        int generation = Interlocked.Increment(ref _hideGeneration);
        _host.PostJson(JsonSerializer.Serialize(new
        {
            version = 1,
            type = "hud.state",
            payload = new { state = "ready", title = _messages.HudReadyTitle, detail = "", generation },
        }));
        _ = Task.Delay(140).ContinueWith(
            _ => Dispatch(() =>
            {
                if (generation != Volatile.Read(ref _hideGeneration)) return;
                _visible = false;
                _host.SetVisible(false);
                WindowsNativeMethods.ShowWindow(_window.Handle, WindowsNativeMethods.SwHide);
            }),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void HideLater()
    {
        int generation = Interlocked.Increment(ref _hideGeneration);
        _ = Task.Delay(1600).ContinueWith(
            _ => Dispatch(() =>
            {
                if (generation == Volatile.Read(ref _hideGeneration)) Hide();
            }),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void ReturnToListeningLater()
    {
        int generation = Interlocked.Increment(ref _hideGeneration);
        _ = Task.Delay(900).ContinueWith(
            _ => Dispatch(() =>
            {
                if (generation == Volatile.Read(ref _hideGeneration) && _dictationActive)
                    Publish("listening", _messages.HudListeningTitle, _mode);
            }),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void Dispatch(Action action)
    {
        if (Volatile.Read(ref _shutdownRequested) != 0) return;
        try
        {
            if (_window != null && _window.IsOwnerThread) action();
            else _dispatcher.Post(action);
        }
        catch (ObjectDisposedException) { }
    }

    private void DispatchHost(Action action)
    {
        if (_window != null && _window.IsOwnerThread) action();
        else _dispatcher.Post(action);
    }

    private void ShutdownOnOwnerThread()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Interlocked.Increment(ref _hideGeneration);
        _appearanceWatcher.Changed -= OnAppearanceChanged;
        _appearanceWatcher.Dispose();
        _host.MessageReceived -= OnMessageReceived;
        _host.Dispose();
        _window.Dispose();
        _dispatcher.Dispose();
    }

    private static void ApplyDpiBounds(nint window, nint parameter)
    {
        if (parameter == nint.Zero) return;
        WindowsRectangle bounds = Marshal.PtrToStructure<WindowsRectangle>(parameter);
        WindowsNativeMethods.SetWindowPos(
            window,
            WindowsNativeMethods.TopMostWindow,
            bounds.Left,
            bounds.Top,
            bounds.Width,
            bounds.Height,
            WindowsNativeMethods.SwpNoActivate);
    }

    private static int Scale(int value, uint dpi)
        => checked((int)Math.Round(value * dpi / (double)DefaultDpi));
}
