using System.Runtime.InteropServices;
using Matraca;

internal static class Native
{
    [DllImport("user32.dll")] internal static extern bool PrintWindow(nint window, nint dc, uint flags);
    [DllImport("dwmapi.dll")] internal static extern int DwmSetWindowAttribute(nint window, int attribute, ref uint value, int size);
    [DllImport("user32.dll")] internal static extern nint SetThreadDpiAwarenessContext(nint context);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(nint window, out WindowsRectangle rect);
    [DllImport("user32.dll")] internal static extern bool GetClientRect(nint window, out WindowsRectangle rect);
    [DllImport("user32.dll")] internal static extern bool ClientToScreen(nint window, ref WindowsPoint point);
    [DllImport("user32.dll")] internal static extern bool ScreenToClient(nint window, ref WindowsPoint point);
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll")] internal static extern bool IsZoomed(nint window);
    [DllImport("user32.dll")] internal static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern nint ChildWindowFromPointEx(nint window, WindowsPoint point, uint flags);
    [DllImport("user32.dll")] internal static extern nint SendMessageW(nint window, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")] internal static extern nint DefWindowProcW(nint window, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")] internal static extern bool PeekMessageW(out WindowsMessage message, nint window, uint min, uint max, uint remove);
    [DllImport("user32.dll")] internal static extern bool TranslateMessage(ref WindowsMessage message);
    [DllImport("user32.dll")] internal static extern nint DispatchMessageW(ref WindowsMessage message);
}
