using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Matraca;

internal sealed class WindowsWebViewWindow : IDisposable
{
    private const int DefaultClientWidth = 1120;
    private const int DefaultClientHeight = 760;
    private const int MinimumWindowWidth = 840;
    private const int MinimumWindowHeight = 620;
    private const uint DefaultDpi = 96;

    private readonly WindowsWebBridge _bridge;
    private readonly WindowsDispatcher _dispatcher;
    private readonly WindowsNativeWindow _window;
    private readonly WindowsWebViewHost _host;
    private Task? _initialization;
    private bool _visible;
    private int _disposed;

    public WindowsWebViewWindow(WindowsWebBridge bridge)
    {
        _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        _dispatcher = new WindowsDispatcher();
        WindowsRectangle bounds = InitialBounds();
        try
        {
            _window = new WindowsNativeWindow(
                "WebViewWindow",
                WindowProcedure,
                "Matraca",
                WindowsNativeMethods.WsResizableWindow,
                0,
                bounds.Left,
                bounds.Top,
                bounds.Width,
                bounds.Height);
            _host = new WindowsWebViewHost(
                _window.Handle,
                Path.Combine(AppContext.BaseDirectory, "Web"),
                Path.Combine(WindowsConfig.Paths.DataDirectory, "WebView2"),
                Dispatch);
        }
        catch
        {
            _window?.Dispose();
            _dispatcher.Dispose();
            throw;
        }

        _host.MessageReceived += OnMessageReceived;
        _bridge.MessageProduced += PostJson;
        _bridge.CloseWindowRequested += Hide;
        _bridge.MinimizeWindowRequested += Minimize;
        _bridge.ToggleMaximizeWindowRequested += ToggleMaximize;
        _bridge.DragWindowRequested += BeginDrag;
    }

    public nint Handle => _window.Handle;

    public void Open(string route)
    {
        VerifyUsable();
        _window.VerifyAccess();
        route = NormalizeRoute(route);
        _bridge.PrepareToOpen(_window.Handle);
        _host.Navigate(route);
        _initialization ??= StartInitialization();

        _visible = true;
        _host.SetVisible(true);
        WindowsNativeMethods.ShowWindow(
            _window.Handle,
            WindowsNativeMethods.IsIconic(_window.Handle)
                ? WindowsNativeMethods.SwRestore
                : WindowsNativeMethods.SwShow);
        WindowsNativeMethods.SetForegroundWindow(_window.Handle);
    }

    public void Hide()
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        Dispatch(HideOnOwnerThread);
    }

    public void Shutdown()
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        _window.VerifyAccess();
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        _bridge.MessageProduced -= PostJson;
        _bridge.CloseWindowRequested -= Hide;
        _bridge.MinimizeWindowRequested -= Minimize;
        _bridge.ToggleMaximizeWindowRequested -= ToggleMaximize;
        _bridge.DragWindowRequested -= BeginDrag;
        _host.MessageReceived -= OnMessageReceived;
        if (_visible) _bridge.WindowClosed();
        _visible = false;
        _host.Dispose();
        _window.Dispose();
        _dispatcher.Dispose();
    }

    public void Dispose() => Shutdown();

    private Task StartInitialization()
    {
        Task task;
        try { task = _host.InitializeAsync(); }
        catch (Exception exception)
        {
            Logger.Error("Falha ao iniciar WebView2 em HWND nativo", exception);
            return Task.CompletedTask;
        }

        _ = task.ContinueWith(
            completed =>
            {
                if (completed.IsFaulted)
                {
                    Logger.Error(
                        "Falha ao iniciar WebView2 em HWND nativo",
                        completed.Exception!.GetBaseException());
                    return;
                }
                if (completed.IsCanceled) return;
                try
                {
                    Dispatch(() =>
                    {
                        if (Volatile.Read(ref _disposed) != 0) return;
                        ResizeHost();
                        _host.SetVisible(_visible);
                    });
                }
                catch (ObjectDisposedException) { }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return task;
    }

    private nint WindowProcedure(nint window, uint message, nint wParam, nint lParam)
    {
        switch (message)
        {
            case WindowsNativeMethods.WmClose:
                HideOnOwnerThread();
                return nint.Zero;
            case WindowsNativeMethods.WmSize:
                ResizeHost();
                return nint.Zero;
            case WindowsNativeMethods.WmMove:
                _host?.NotifyParentWindowPositionChanged();
                break;
            case WindowsNativeMethods.WmGetMinMaxInfo:
                ApplyMinimumSize(window, lParam);
                return nint.Zero;
            case WindowsNativeMethods.WmDpiChanged:
                ApplyDpiBounds(window, lParam);
                return nint.Zero;
        }

        return WindowsNativeMethods.DefWindowProcW(window, message, wParam, lParam);
    }

    private void ResizeHost()
    {
        if (_host == null || _window == null) return;
        if (!WindowsNativeMethods.GetClientRect(_window.Handle, out WindowsRectangle client)) return;
        _host.Resize(client.Width, client.Height);
    }

    private void HideOnOwnerThread()
    {
        if (Volatile.Read(ref _disposed) != 0 || !_visible) return;
        _host.SetVisible(false);
        WindowsNativeMethods.ShowWindow(_window.Handle, WindowsNativeMethods.SwHide);
        _visible = false;
        _bridge.WindowClosed();
    }

    private void Minimize()
        => Dispatch(() => WindowsNativeMethods.ShowWindow(
            _window.Handle,
            WindowsNativeMethods.SwMinimize));

    private void ToggleMaximize()
        => Dispatch(() => WindowsNativeMethods.ShowWindow(
            _window.Handle,
            WindowsNativeMethods.IsZoomed(_window.Handle)
                ? WindowsNativeMethods.SwRestore
                : WindowsNativeMethods.SwMaximize));

    private void BeginDrag()
        => Dispatch(() =>
        {
            WindowsNativeMethods.ReleaseCapture();
            WindowsNativeMethods.SendMessageW(
                _window.Handle,
                WindowsNativeMethods.WmNcLButtonDown,
                WindowsNativeMethods.HtCaption,
                nint.Zero);
        });

    private void OnMessageReceived(string json)
        => Observe(HandleMessageAsync(json));

    private async Task HandleMessageAsync(string json)
    {
        string? response = await _bridge.HandleAsync(json).ConfigureAwait(false);
        if (response != null) PostJson(response);
    }

    private void PostJson(string json)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        try
        {
            Dispatch(() =>
            {
                if (Volatile.Read(ref _disposed) == 0) _host.PostJson(json);
            });
        }
        catch (ObjectDisposedException) { }
    }

    private void Dispatch(Action action)
    {
        if (_window != null && _window.IsOwnerThread) action();
        else _dispatcher.Post(action);
    }

    private void VerifyUsable()
        => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

    private static WindowsRectangle InitialBounds()
    {
        uint dpi = WindowsNativeMethods.GetDpiForSystem();
        if (dpi == 0) dpi = DefaultDpi;
        var bounds = new WindowsRectangle
        {
            Right = Scale(DefaultClientWidth, dpi),
            Bottom = Scale(DefaultClientHeight, dpi),
        };
        if (!WindowsNativeMethods.AdjustWindowRectExForDpi(
                ref bounds,
                WindowsNativeMethods.WsResizableWindow,
                false,
                0,
                dpi))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Falha ao calcular o tamanho da janela WebView2.");

        WindowsRectangle workArea;
        if (!WindowsNativeMethods.SystemParametersInfo(
                WindowsNativeMethods.SpiGetWorkArea,
                0,
                out workArea,
                0))
        {
            workArea = new WindowsRectangle
            {
                Right = WindowsNativeMethods.GetSystemMetrics(WindowsNativeMethods.SmCxScreen),
                Bottom = WindowsNativeMethods.GetSystemMetrics(WindowsNativeMethods.SmCyScreen),
            };
        }

        int width = bounds.Width;
        int height = bounds.Height;
        int x = workArea.Left + Math.Max(0, (workArea.Width - width) / 2);
        int y = workArea.Top + Math.Max(0, (workArea.Height - height) / 2);
        return new WindowsRectangle { Left = x, Top = y, Right = x + width, Bottom = y + height };
    }

    private static void ApplyMinimumSize(nint window, nint parameter)
    {
        if (parameter == nint.Zero) return;
        WindowsMinMaxInfo info = Marshal.PtrToStructure<WindowsMinMaxInfo>(parameter);
        uint dpi = WindowsNativeMethods.GetDpiForWindow(window);
        if (dpi == 0) dpi = DefaultDpi;
        info.MinimumTrackSize.X = Scale(MinimumWindowWidth, dpi);
        info.MinimumTrackSize.Y = Scale(MinimumWindowHeight, dpi);
        Marshal.StructureToPtr(info, parameter, false);
    }

    private static void ApplyDpiBounds(nint window, nint parameter)
    {
        if (parameter == nint.Zero) return;
        WindowsRectangle bounds = Marshal.PtrToStructure<WindowsRectangle>(parameter);
        WindowsNativeMethods.SetWindowPos(
            window,
            nint.Zero,
            bounds.Left,
            bounds.Top,
            bounds.Width,
            bounds.Height,
            WindowsNativeMethods.SwpNoZOrder | WindowsNativeMethods.SwpNoActivate);
    }

    private static int Scale(int value, uint dpi)
        => checked((int)Math.Round(value * dpi / (double)DefaultDpi));

    private static string NormalizeRoute(string route)
        => route is "history" or "settings" or "microphone" or "onboarding" ? route : "home";

    private static void Observe(Task task)
        => _ = task.ContinueWith(
            failed => Logger.Error(
                "Falha ao processar mensagem da interface WebView2 nativa",
                failed.Exception!.GetBaseException()),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
}
