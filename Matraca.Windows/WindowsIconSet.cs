using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Matraca;

internal sealed class WindowsIconSet : IDisposable
{
    private const int ApplicationIconResource = 32512;
    private const int ExclamationIconResource = 32515;
    private const int InformationIconResource = 32516;

    private readonly bool _ownsIdle;
    private readonly bool _ownsRecording;
    private readonly bool _ownsBusy;
    private int _disposed;

    public WindowsIconSet()
    {
        (Idle, _ownsIdle) = Load("app.ico", ApplicationIconResource);
        try
        {
            (Recording, _ownsRecording) = Load("rec.ico", ExclamationIconResource);
            try
            {
                (Busy, _ownsBusy) = Load("busy.ico", InformationIconResource);
            }
            catch
            {
                Release(Recording, _ownsRecording);
                throw;
            }
        }
        catch
        {
            Release(Idle, _ownsIdle);
            throw;
        }
    }

    public nint Idle { get; }

    public nint Recording { get; }

    public nint Busy { get; }

    private static (nint Handle, bool Owned) Load(string fileName, int fallbackResource)
    {
        string path = Path.Combine(AppContext.BaseDirectory, fileName);
        nint icon = WindowsNativeMethods.LoadImageFromFile(
            nint.Zero,
            path,
            WindowsNativeMethods.ImageIcon,
            0,
            0,
            WindowsNativeMethods.LrDefaultSize | WindowsNativeMethods.LrLoadFromFile);
        if (icon != nint.Zero) return (icon, true);

        int fileError = Marshal.GetLastWin32Error();
        Logger.Warn($"Falha ao carregar icone {fileName} (Win32 {fileError}); usando icone do sistema.");
        icon = WindowsNativeMethods.LoadImageResource(
            nint.Zero,
            new nint(fallbackResource),
            WindowsNativeMethods.ImageIcon,
            0,
            0,
            WindowsNativeMethods.LrDefaultSize | WindowsNativeMethods.LrShared);
        if (icon == nint.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Falha ao carregar o fallback de {fileName}.");
        return (icon, false);
    }

    private static void Release(nint icon, bool owned)
    {
        if (owned && icon != nint.Zero) WindowsNativeMethods.DestroyIcon(icon);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Release(Busy, _ownsBusy);
        Release(Recording, _ownsRecording);
        Release(Idle, _ownsIdle);
    }
}
