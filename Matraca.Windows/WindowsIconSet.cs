using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Matraca;

// Owns every LoadImage handle; consumers borrow until their windows are destroyed.
internal sealed class WindowsIconSet : IDisposable
{
    private readonly Dictionary<(string File, int Size), nint> _handles = new();
    private int _disposed;

    public nint Tray(ShellState state, string theme, int size) => Load(
        $"Icons/{state switch
        {
            ShellState.Recording => "recording",
            ShellState.Busy or ShellState.Writing => "busy",
            ShellState.Error => "error",
            _ => "idle",
        }}-{theme}.ico", size);

    public nint Application(string palette, string theme, int size)
        => Load($"Icons/app-{Config.NormalizePalette(palette)}-{theme}.ico", size);

    public static string ApplicationTheme(string mode)
        => Config.NormalizeThemeMode(mode) switch
        {
            "light" => "light",
            "dark" => "dark",
            _ => SystemTheme("AppsUseLightTheme"),
        };

    // The taskbar background is independent of the application's chosen theme.
    public static string TaskbarTheme() => SystemTheme("SystemUsesLightTheme");

    private static string SystemTheme(string value)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue(value) is int light && light == 0 ? "dark" : "light";
        }
        catch (Exception exception) when (exception is System.Security.SecurityException
            or UnauthorizedAccessException or IOException)
        {
            return "light";
        }
    }

    private nint Load(string file, int size)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (_handles.TryGetValue((file, size), out nint cached)) return cached;
        nint icon = WindowsNativeMethods.LoadImageFromFile(nint.Zero,
            Path.Combine(AppContext.BaseDirectory, file), WindowsNativeMethods.ImageIcon,
            size, size, WindowsNativeMethods.LrLoadFromFile);
        if (icon == nint.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Falha ao carregar icone oficial {file}.");
        _handles.Add((file, size), icon);
        return icon;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        foreach (nint icon in _handles.Values) WindowsNativeMethods.DestroyIcon(icon);
        _handles.Clear();
    }
}
