using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Matraca;

internal sealed class WindowsNativeWindow : IDisposable
{
    private readonly string _className;
    private readonly nint _instance;
    private readonly uint _ownerThreadId;
    private readonly WindowsWindowProcedure _managedProcedure;
    private readonly WindowsWindowProcedure _nativeProcedure;
    private int _disposed;

    public WindowsNativeWindow(string name, WindowsWindowProcedure procedure, bool messageOnly)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(procedure);

        _ownerThreadId = WindowsNativeMethods.GetCurrentThreadId();
        _instance = WindowsNativeMethods.GetModuleHandleW(null);
        _className = $"Matraca.{name}.{Guid.NewGuid():N}";
        _managedProcedure = procedure;
        _nativeProcedure = Dispatch;

        var windowClass = new WindowsWindowClass
        {
            Size = (uint)Marshal.SizeOf<WindowsWindowClass>(),
            WindowProcedure = _nativeProcedure,
            Instance = _instance,
            ClassName = _className,
        };

        if (WindowsNativeMethods.RegisterClassEx(ref windowClass) == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Falha ao registrar a janela nativa {name}.");

        Handle = WindowsNativeMethods.CreateWindowEx(
            0,
            _className,
            string.Empty,
            0,
            0,
            0,
            0,
            0,
            messageOnly ? WindowsNativeMethods.MessageOnlyWindow : nint.Zero,
            nint.Zero,
            _instance,
            nint.Zero);

        if (Handle != nint.Zero) return;

        int error = Marshal.GetLastWin32Error();
        WindowsNativeMethods.UnregisterClass(_className, _instance);
        throw new Win32Exception(error, $"Falha ao criar a janela nativa {name}.");
    }

    public nint Handle { get; private set; }

    public bool IsOwnerThread => WindowsNativeMethods.GetCurrentThreadId() == _ownerThreadId;

    public void VerifyAccess()
    {
        if (!IsOwnerThread)
            throw new InvalidOperationException("A janela nativa deve ser acessada pela thread que a criou.");
    }

    private nint Dispatch(nint window, uint message, nint wParam, nint lParam)
    {
        try
        {
            return _managedProcedure(window, message, wParam, lParam);
        }
        catch (Exception exception)
        {
            Logger.Error("Falha no procedimento de janela Win32", exception);
            return WindowsNativeMethods.DefWindowProcW(window, message, wParam, lParam);
        }
    }

    public void Dispose()
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        VerifyAccess();
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        if (Handle != nint.Zero)
        {
            WindowsNativeMethods.DestroyWindow(Handle);
            Handle = nint.Zero;
        }

        WindowsNativeMethods.UnregisterClass(_className, _instance);
    }
}
