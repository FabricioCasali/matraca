using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Matraca;

internal sealed class WindowsOleScope : IDisposable
{
    private readonly int _threadId;
    private int _disposed;

    private WindowsOleScope()
    {
        _threadId = Environment.CurrentManagedThreadId;
    }

    internal static bool TryEnter([NotNullWhen(true)] out WindowsOleScope? scope)
    {
        scope = null;
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
        {
            Logger.Warn("Clipboard OLE exige uma thread STA.");
            return false;
        }

        int result = WindowsNativeMethods.OleInitialize(nint.Zero);
        if (result < 0)
        {
            string message = Marshal.GetExceptionForHR(result)?.Message ?? $"HRESULT 0x{result:X8}";
            Logger.Warn("Falha ao inicializar OLE para o clipboard: " + message);
            return false;
        }

        scope = new WindowsOleScope();
        return true;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        if (Environment.CurrentManagedThreadId != _threadId)
            throw new InvalidOperationException("OLE deve ser liberado na thread que o inicializou.");
        WindowsNativeMethods.OleUninitialize();
    }
}
