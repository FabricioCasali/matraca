using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;

namespace Matraca;

internal sealed class WindowsFocusIndicator : IDisposable
{
    private const nuint TrackingTimerId = 1;
    private const uint TrackingIntervalMilliseconds = 100;
    private const int MinimumTargetSize = 20;
    private const int HitTestTransparent = -1;

    private static readonly Guid VirtualDesktopManagerClassId = new("aa509086-5ca9-4c25-8f95-589d3c07b48a");
    private static IWindowsVirtualDesktopManager? _virtualDesktopManager;
    private static bool _virtualDesktopManagerAttempted;

    private readonly WindowsDispatcher _dispatcher;
    private readonly WindowsNativeWindow _window;
    private readonly int _thickness;
    private nint _brush;
    private nint _lastTarget;
    private nint _pinnedTarget;
    private WindowsRectangle _lastBounds;
    private bool _timerRunning;
    private bool _visible;
    private int _shutdownRequested;
    private int _disposed;

    public WindowsFocusIndicator(Color color, int thickness, float opacity)
    {
        _thickness = Math.Max(1, thickness);
        _dispatcher = new WindowsDispatcher();

        try
        {
            _brush = CreateBrush(color);
            _window = new WindowsNativeWindow(
                "FocusIndicator",
                WindowProcedure,
                string.Empty,
                WindowsNativeMethods.WsPopup,
                WindowsNativeMethods.WsExLayered
                    | WindowsNativeMethods.WsExTransparent
                    | WindowsNativeMethods.WsExToolWindow
                    | WindowsNativeMethods.WsExNoActivate,
                0,
                0,
                0,
                0);

            byte alpha = (byte)Math.Round(Math.Clamp(opacity, 0.1f, 1f) * byte.MaxValue);
            if (!WindowsNativeMethods.SetLayeredWindowAttributes(
                    _window.Handle,
                    0,
                    alpha,
                    WindowsNativeMethods.LayeredWindowAlpha))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Falha ao configurar a opacidade da moldura.");
        }
        catch
        {
            _window?.Dispose();
            _dispatcher.Dispose();
            WindowsNativeMethods.DeleteObject(_brush);
            _brush = nint.Zero;
            throw;
        }
    }

    public void SetPinned(nint window)
    {
        VerifyUsable();
        _window.VerifyAccess();
        _pinnedTarget = window;
        _lastTarget = nint.Zero;
    }

    public void SetColor(Color color)
    {
        VerifyUsable();
        _window.VerifyAccess();

        nint replacement = CreateBrush(color);
        nint previous = _brush;
        _brush = replacement;
        WindowsNativeMethods.InvalidateRect(_window.Handle, nint.Zero, erase: false);
        WindowsNativeMethods.DeleteObject(previous);
    }

    public void ShowBorder()
    {
        VerifyUsable();
        _window.VerifyAccess();
        _lastTarget = nint.Zero;
        Track();

        if (_timerRunning) return;
        if (WindowsNativeMethods.SetTimer(
                _window.Handle,
                TrackingTimerId,
                TrackingIntervalMilliseconds,
                nint.Zero) != 0)
        {
            _timerRunning = true;
            return;
        }

        HideOverlay();
        Logger.Warn("Nao foi possivel iniciar o timer Win32 da moldura de foco.");
    }

    public void HideBorder()
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        _window.VerifyAccess();
        StopTimer();
        _lastTarget = nint.Zero;
        HideOverlay();
    }

    private nint WindowProcedure(nint window, uint message, nint wParam, nint lParam)
    {
        if (message == WindowsNativeMethods.WmTimer && (nuint)wParam == TrackingTimerId)
        {
            Track();
            return nint.Zero;
        }

        if (message == WindowsNativeMethods.WmPaint)
        {
            Paint(window);
            return nint.Zero;
        }

        if (message == WindowsNativeMethods.WmNcHitTest)
            return HitTestTransparent;

        return WindowsNativeMethods.DefWindowProcW(window, message, wParam, lParam);
    }

    private void Track()
    {
        nint target = _pinnedTarget != nint.Zero
            ? _pinnedTarget
            : WindowsNativeMethods.GetForegroundWindow();
        bool outsideCurrentDesktop = _pinnedTarget != nint.Zero && !IsOnCurrentDesktop(target);

        if (target == nint.Zero
            || target == _window.Handle
            || !WindowsNativeMethods.IsWindow(target)
            || WindowsNativeMethods.IsIconic(target)
            || outsideCurrentDesktop
            || !TryGetBounds(target, out WindowsRectangle bounds)
            || bounds.Width < MinimumTargetSize
            || bounds.Height < MinimumTargetSize)
        {
            HideOverlay();
            _lastTarget = nint.Zero;
            return;
        }

        if (_visible && target == _lastTarget && BoundsEqual(bounds, _lastBounds)) return;

        int thickness = Math.Min(_thickness, (Math.Min(bounds.Width, bounds.Height) - 1) / 2);
        if (!ApplyHollowRegion(bounds.Width, bounds.Height, thickness)
            || !WindowsNativeMethods.SetWindowPos(
                _window.Handle,
                WindowsNativeMethods.TopMostWindow,
                bounds.Left,
                bounds.Top,
                bounds.Width,
                bounds.Height,
                WindowsNativeMethods.SwpNoActivate | WindowsNativeMethods.SwpShowWindow))
        {
            HideOverlay();
            _lastTarget = nint.Zero;
            return;
        }

        _lastTarget = target;
        _lastBounds = bounds;
        _visible = true;
    }

    private bool ApplyHollowRegion(int width, int height, int thickness)
    {
        nint outer = WindowsNativeMethods.CreateRectRgn(0, 0, width, height);
        nint inner = WindowsNativeMethods.CreateRectRgn(
            thickness,
            thickness,
            width - thickness,
            height - thickness);

        if (outer == nint.Zero || inner == nint.Zero)
        {
            if (outer != nint.Zero) WindowsNativeMethods.DeleteObject(outer);
            if (inner != nint.Zero) WindowsNativeMethods.DeleteObject(inner);
            return false;
        }

        bool combined = WindowsNativeMethods.CombineRgn(
            outer,
            outer,
            inner,
            WindowsNativeMethods.RegionDifference) != 0;
        WindowsNativeMethods.DeleteObject(inner);

        if (!combined || WindowsNativeMethods.SetWindowRgn(_window.Handle, outer, redraw: true) == 0)
        {
            WindowsNativeMethods.DeleteObject(outer);
            return false;
        }

        // SetWindowRgn transfers ownership of the successful region to Windows.
        return true;
    }

    private void Paint(nint window)
    {
        nint deviceContext = WindowsNativeMethods.BeginPaint(window, out WindowsPaintStruct paint);
        try
        {
            if (deviceContext == nint.Zero || !WindowsNativeMethods.GetClientRect(window, out WindowsRectangle bounds))
                return;

            WindowsNativeMethods.FillRect(deviceContext, ref bounds, _brush);
        }
        finally
        {
            WindowsNativeMethods.EndPaint(window, ref paint);
        }
    }

    private void HideOverlay()
    {
        if (!_visible) return;
        _visible = false;
        WindowsNativeMethods.ShowWindow(_window.Handle, WindowsNativeMethods.SwHide);
    }

    private void StopTimer()
    {
        if (!_timerRunning) return;
        WindowsNativeMethods.KillTimer(_window.Handle, TrackingTimerId);
        _timerRunning = false;
    }

    private static bool TryGetBounds(nint window, out WindowsRectangle bounds)
    {
        if (WindowsNativeMethods.DwmGetWindowAttribute(
                window,
                WindowsNativeMethods.DwmExtendedFrameBounds,
                out bounds,
                Marshal.SizeOf<WindowsRectangle>()) == 0)
            return true;

        return WindowsNativeMethods.GetWindowRect(window, out bounds);
    }

    private static bool IsOnCurrentDesktop(nint window)
    {
        try
        {
            if (!_virtualDesktopManagerAttempted)
            {
                _virtualDesktopManagerAttempted = true;
                Type? managerType = Type.GetTypeFromCLSID(VirtualDesktopManagerClassId);
                if (managerType != null)
                    _virtualDesktopManager = Activator.CreateInstance(managerType) as IWindowsVirtualDesktopManager;
                if (_virtualDesktopManager == null)
                    Logger.Warn("VirtualDesktopManager indisponivel; a moldura ignora desktops virtuais.");
            }

            if (_virtualDesktopManager == null) return true;
            int result = _virtualDesktopManager.IsWindowOnCurrentVirtualDesktop(window, out int onCurrentDesktop);
            return result != 0 || onCurrentDesktop != 0;
        }
        catch (Exception exception)
        {
            Logger.Warn("Falha ao consultar o desktop virtual da janela: " + exception.Message);
            return true;
        }
    }

    private static nint CreateBrush(Color color)
    {
        uint colorReference = (uint)(color.R | color.G << 8 | color.B << 16);
        nint brush = WindowsNativeMethods.CreateSolidBrush(colorReference);
        if (brush == nint.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Falha ao criar o pincel da moldura.");
        return brush;
    }

    private static bool BoundsEqual(WindowsRectangle left, WindowsRectangle right)
        => left.Left == right.Left
            && left.Top == right.Top
            && left.Right == right.Right
            && left.Bottom == right.Bottom;

    private void VerifyUsable()
        => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _shutdownRequested, 1) != 0) return;
        if (_window.IsOwnerThread)
        {
            DisposeOnOwnerThread();
            return;
        }

        try { _dispatcher.Post(DisposeOnOwnerThread); }
        catch (ObjectDisposedException) { }
    }

    private void DisposeOnOwnerThread()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        StopTimer();
        HideOverlay();
        _window.Dispose();
        WindowsNativeMethods.DeleteObject(_brush);
        _brush = nint.Zero;
        _dispatcher.Dispose();
    }
}
