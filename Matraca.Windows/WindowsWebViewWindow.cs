using System.Runtime.InteropServices;

namespace Matraca;

internal sealed class WindowsWebViewWindow : IDisposable
{
    private const int DefaultClientWidth = 1200;
    private const int DefaultClientHeight = 820;
    private const int MinimumWindowWidth = 840;
    private const int MinimumWindowHeight = 620;
    private const uint DefaultDpi = 96;

    private readonly WindowsWebBridge _bridge;
    private readonly WindowsIconSet _icons;
    private Config _appearance;
    private readonly WindowsDispatcher _dispatcher;
    private readonly WindowsNativeWindow _window;
    private readonly WindowsWebViewHost _host;
    private Task? _initialization;
    private bool _visible;
    private bool _paintFrame;
    private uint _frameColor;
    private int _disposed;

    public WindowsWebViewWindow(WindowsWebBridge bridge, WindowsIconSet icons, Config appearance)
    {
        _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        _icons = icons;
        _appearance = appearance;
        _dispatcher = new WindowsDispatcher();
        WindowsRectangle bounds = InitialBounds();
        try
        {
            _window = new WindowsNativeWindow(
                "WebViewWindow",
                WindowProcedure,
                "Matraca",
                WindowsNativeMethods.WsFramelessResizableWindow,
                0,
                bounds.Left,
                bounds.Top,
                bounds.Width,
                bounds.Height);
            ApplyAppearance(appearance);
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

    public void ApplyAppearance(Config config)
    {
        _window.VerifyAccess();
        if (Volatile.Read(ref _disposed) != 0) return;
        _appearance = config;
        string theme = WindowsIconSet.ApplicationTheme(config.ThemeMode);
        ApplyFrameAppearance(theme);
        uint dpi = WindowsNativeMethods.GetDpiForWindow(_window.Handle);
        if (dpi == 0) dpi = DefaultDpi;
        // WM_SETICON borrows these cached handles; WindowsApplication disposes them
        // only after this window and the tray have been destroyed.
        WindowsNativeMethods.SendMessageW(_window.Handle, 0x0080, 0,
            _icons.Application(config.Palette, theme, Scale(16, dpi)));
        WindowsNativeMethods.SendMessageW(_window.Handle, 0x0080, 1,
            _icons.Application(config.Palette, theme, Scale(32, dpi)));
    }

    private void ApplyFrameAppearance(string theme)
    {
        var contrast = new WindowsHighContrast { Size = (uint)Marshal.SizeOf<WindowsHighContrast>() };
        bool custom = WindowsNativeMethods.GetHighContrast(0x0042, contrast.Size, ref contrast, 0)
            && (contrast.Flags & 1) == 0; // SPI_GETHIGHCONTRAST / HCF_HIGHCONTRASTON
        _frameColor = theme == "dark" ? WindowsPanelColors.Dark : WindowsPanelColors.Light;
        // Border/caption colors do not cover the full NCCALCSIZE band. Own its
        // painting instead; use the system frame in high contrast or on API failure.
        int policy = custom ? 1 : 0; // DWMNCRP_DISABLED / DWMNCRP_USEWINDOWSTYLE
        _paintFrame = custom;
        if (WindowsNativeMethods.DwmSetWindowAttribute(_window.Handle, 2, ref policy, sizeof(int)) < 0)
            _paintFrame = false;
        WindowsNativeMethods.RedrawWindow(_window.Handle, 0, 0, 0x0401); // RDW_FRAME | RDW_INVALIDATE
    }

    private bool PaintFrame(nint window)
    {
        if (!_paintFrame || !WindowsNativeMethods.GetWindowRect(window, out WindowsRectangle outer)
            || !WindowsNativeMethods.GetClientRect(window, out WindowsRectangle client)) return false;
        var origin = new WindowsPoint();
        if (!WindowsNativeMethods.ClientToScreen(window, ref origin)) return false;
        nint dc = WindowsNativeMethods.GetWindowDC(window);
        if (dc == 0) return false;
        nint brush = WindowsNativeMethods.CreateSolidBrush(_frameColor);
        try
        {
            if (brush == 0) return false;
            int x = origin.X - outer.Left, y = origin.Y - outer.Top;
            if (WindowsNativeMethods.ExcludeClipRect(dc, x, y, x + client.Width, y + client.Height) == 0)
                return false;
            var bounds = new WindowsRectangle { Right = outer.Width, Bottom = outer.Height };
            return WindowsNativeMethods.FillRect(dc, ref bounds, brush) != 0;
        }
        finally
        {
            if (brush != 0) WindowsNativeMethods.DeleteObject(brush);
            WindowsNativeMethods.ReleaseDC(window, dc);
        }
    }

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
            case WindowsNativeMethods.WmNcPaint:
                if (PaintFrame(window)) return nint.Zero;
                break;
            case WindowsNativeMethods.WmNcActivate:
                if (_paintFrame && !WindowsNativeMethods.IsIconic(window))
                    return WindowsNativeMethods.DefWindowProcW(window, message, wParam, new nint(-1));
                break;
            case 0x001A: // WM_SETTINGCHANGE
            case 0x031A: // WM_THEMECHANGED
            case 0x031E: // WM_DWMCOMPOSITIONCHANGED
                if (_window != null) ApplyAppearance(_appearance);
                break;
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
            case WindowsNativeMethods.WmNcCalcSize:
                ApplyClientBounds(window, lParam);
                return nint.Zero;
            case WindowsNativeMethods.WmNcHitTest:
                return HitTestResizeBorder(window, lParam);
            case WindowsNativeMethods.WmDpiChanged:
                ApplyDpiBounds(window, lParam);
                ResizeHost();
                ApplyAppearance(_appearance);
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
            if (!WindowsNativeMethods.GetCursorPos(out WindowsPoint point)) return;
            WindowsNativeMethods.ReleaseCapture();
            WindowsNativeMethods.SendMessageW(
                _window.Handle,
                WindowsNativeMethods.WmNcLButtonDown,
                WindowsNativeMethods.HtCaption,
                (nint)((point.X & 0xFFFF) | ((point.Y & 0xFFFF) << 16)));
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
        WindowsPoint border = ResizeBorder(dpi);
        var bounds = new WindowsRectangle
        {
            Right = Scale(DefaultClientWidth, dpi) + 2 * border.X,
            Bottom = Scale(DefaultClientHeight, dpi) + 2 * border.Y,
        };

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

        int width = Math.Min(bounds.Width, workArea.Width);
        int height = Math.Min(bounds.Height, workArea.Height);
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
        nint monitor = WindowsNativeMethods.MonitorFromWindow(window, WindowsNativeMethods.MonitorDefaultToNearest);
        var monitorInfo = new WindowsMonitorInfo { Size = (uint)Marshal.SizeOf<WindowsMonitorInfo>() };
        if (monitor != nint.Zero && WindowsNativeMethods.GetMonitorInfo(monitor, ref monitorInfo))
        {
            // The minimum must not undo the initial work-area clamp on small/high-DPI screens.
            info.MinimumTrackSize.X = Math.Min(info.MinimumTrackSize.X, monitorInfo.WorkArea.Width);
            info.MinimumTrackSize.Y = Math.Min(info.MinimumTrackSize.Y, monitorInfo.WorkArea.Height);
        }
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

    private static WindowsPoint ResizeBorder(uint dpi)
    {
        if (dpi == 0) dpi = DefaultDpi;
        int padding = WindowsNativeMethods.GetSystemMetricsForDpi(92, dpi); // SM_CXPADDEDBORDER
        return new WindowsPoint
        {
            X = WindowsNativeMethods.GetSystemMetricsForDpi(32, dpi) + padding, // SM_CXSIZEFRAME
            Y = WindowsNativeMethods.GetSystemMetricsForDpi(33, dpi) + padding, // SM_CYSIZEFRAME
        };
    }

    private static void ApplyClientBounds(nint window, nint parameter)
    {
        if (parameter == nint.Zero) return;
        // RECT is also the first field of NCCALCSIZE_PARAMS (wParam == TRUE).
        // Keep the resize frame outside the client: a windowed WebView2 child
        // consumes hit tests wherever its bounds cover the parent.
        WindowsRectangle bounds = Marshal.PtrToStructure<WindowsRectangle>(parameter);
        if (WindowsNativeMethods.IsZoomed(window))
        {
            nint monitor = WindowsNativeMethods.MonitorFromWindow(window, WindowsNativeMethods.MonitorDefaultToNearest);
            var info = new WindowsMonitorInfo { Size = (uint)Marshal.SizeOf<WindowsMonitorInfo>() };
            if (monitor != nint.Zero && WindowsNativeMethods.GetMonitorInfo(monitor, ref info))
            {
                bounds.Left = Math.Max(bounds.Left, info.WorkArea.Left);
                bounds.Top = Math.Max(bounds.Top, info.WorkArea.Top);
                bounds.Right = Math.Min(bounds.Right, info.WorkArea.Right);
                bounds.Bottom = Math.Min(bounds.Bottom, info.WorkArea.Bottom);
            }
        }
        else
        {
            WindowsPoint border = ResizeBorder(WindowsNativeMethods.GetDpiForWindow(window));
            int x = Math.Min(border.X, Math.Max(0, bounds.Width / 2));
            int y = Math.Min(border.Y, Math.Max(0, bounds.Height / 2));
            bounds.Left += x;
            bounds.Right -= x;
            bounds.Top += y;
            bounds.Bottom -= y;
        }
        Marshal.StructureToPtr(bounds, parameter, false);
    }

    private static nint HitTestResizeBorder(nint window, nint parameter)
    {
        if (WindowsNativeMethods.IsZoomed(window)
            || !WindowsNativeMethods.GetWindowRect(window, out WindowsRectangle bounds))
            return WindowsNativeMethods.HtClient;

        long coordinates = parameter.ToInt64();
        int x = (short)(coordinates & 0xFFFF);
        int y = (short)((coordinates >> 16) & 0xFFFF);
        WindowsPoint border = ResizeBorder(WindowsNativeMethods.GetDpiForWindow(window));
        bool left = x < bounds.Left + border.X;
        bool right = x >= bounds.Right - border.X;
        bool top = y < bounds.Top + border.Y;
        bool bottom = y >= bounds.Bottom - border.Y;

        if (top) return left ? WindowsNativeMethods.HtTopLeft
            : right ? WindowsNativeMethods.HtTopRight
            : WindowsNativeMethods.HtTop;
        if (bottom) return left ? WindowsNativeMethods.HtBottomLeft
            : right ? WindowsNativeMethods.HtBottomRight
            : WindowsNativeMethods.HtBottom;
        if (left) return WindowsNativeMethods.HtLeft;
        if (right) return WindowsNativeMethods.HtRight;
        return WindowsNativeMethods.HtClient;
    }

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
