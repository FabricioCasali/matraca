using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Matraca;

internal sealed class WindowsMessageLoop
{
    public int Run()
    {
        while (true)
        {
            int result = WindowsNativeMethods.GetMessageW(out WindowsMessage message, nint.Zero, 0, 0);
            if (result == 0) return unchecked((int)message.WParam);
            if (result == -1)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Falha no loop de mensagens Win32.");

            WindowsNativeMethods.TranslateMessage(ref message);
            WindowsNativeMethods.DispatchMessageW(ref message);
        }
    }

    public void Exit(int exitCode = 0)
        => WindowsNativeMethods.PostQuitMessage(exitCode);
}
