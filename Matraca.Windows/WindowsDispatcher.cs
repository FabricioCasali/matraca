using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Matraca;

internal sealed class WindowsDispatcher : IDisposable
{
    private const uint DispatchMessage = WindowsNativeMethods.WmApp + 0x31;

    private readonly object _gate = new();
    private readonly LinkedList<Action> _actions = new();
    private readonly WindowsNativeWindow _window;
    private int _disposed;

    public WindowsDispatcher()
    {
        // Broadcasts such as WM_POWERBROADCAST do not reach HWND_MESSAGE windows.
        _window = new WindowsNativeWindow("Dispatcher", WindowProcedure, messageOnly: false);
    }

    public nint WindowHandle => _window.Handle;

    public bool IsDispatchThread => _window.IsOwnerThread;

    public event Action<uint, nint, nint>? UnhandledMessage;

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            LinkedListNode<Action> pendingAction = _actions.AddLast(action);
            if (WindowsNativeMethods.PostMessageW(_window.Handle, DispatchMessage, nint.Zero, nint.Zero)) return;

            _actions.Remove(pendingAction);
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Falha ao publicar uma acao no dispatcher Win32.");
        }
    }

    private nint WindowProcedure(nint window, uint message, nint wParam, nint lParam)
    {
        if (message != DispatchMessage)
        {
            NotifyUnhandledMessage(message, wParam, lParam);
            return WindowsNativeMethods.DefWindowProcW(window, message, wParam, lParam);
        }

        Action? action;
        lock (_gate)
        {
            action = _actions.First?.Value;
            if (_actions.First != null) _actions.RemoveFirst();
        }

        if (action == null) return nint.Zero;

        try
        {
            action();
        }
        catch (Exception exception)
        {
            Logger.Error("Falha em acao do dispatcher Win32", exception);
        }

        return nint.Zero;
    }

    private void NotifyUnhandledMessage(uint message, nint wParam, nint lParam)
    {
        Delegate[] handlers = UnhandledMessage?.GetInvocationList() ?? [];
        foreach (Action<uint, nint, nint> handler in handlers)
        {
            try
            {
                handler(message, wParam, lParam);
            }
            catch (Exception exception)
            {
                try { Logger.Error("Falha em observador de mensagem Win32", exception); }
                catch { }
            }
        }
    }

    public void Dispose()
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        _window.VerifyAccess();

        lock (_gate)
        {
            if (_disposed != 0) return;
            _disposed = 1;
            _actions.Clear();
            UnhandledMessage = null;
        }

        _window.Dispose();
    }
}
