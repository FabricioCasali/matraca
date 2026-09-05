using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Matraca;

internal sealed class WindowsClipboardOwner : IDisposable
{
    private nint _handle;

    private WindowsClipboardOwner(nint handle)
    {
        _handle = handle;
    }

    internal nint Handle
        => _handle != nint.Zero
            ? _handle
            : throw new ObjectDisposedException(nameof(WindowsClipboardOwner));

    internal static bool TryCreate([NotNullWhen(true)] out WindowsClipboardOwner? owner)
    {
        nint handle = WindowsNativeMethods.CreateWindowEx(
            0,
            "STATIC",
            "",
            0,
            0,
            0,
            0,
            0,
            WindowsNativeMethods.MessageOnlyWindow,
            nint.Zero,
            WindowsNativeMethods.GetModuleHandleW(null),
            nint.Zero);
        if (handle == nint.Zero)
        {
            Logger.Warn($"Nao consegui criar o owner do clipboard (err {Marshal.GetLastWin32Error()}).");
            owner = null;
            return false;
        }

        owner = new WindowsClipboardOwner(handle);
        return true;
    }

    public void Dispose()
    {
        nint handle = Interlocked.Exchange(ref _handle, nint.Zero);
        if (handle != nint.Zero && !WindowsNativeMethods.DestroyWindow(handle))
            Logger.Warn($"Nao consegui destruir o owner do clipboard (err {Marshal.GetLastWin32Error()}).");
    }
}
