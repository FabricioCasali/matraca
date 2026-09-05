using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using ComDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace Matraca;

internal static class WindowsClipboard
{
    private const int Attempts = 5;
    private const int RetryDelayMilliseconds = 30;

    internal static bool TryWriteText(string text)
    {
        if (!TryWriteText(text, out WindowsClipboardSnapshot? snapshot)) return false;
        snapshot.Dispose();
        return true;
    }

    internal static bool TryWriteText(
        string text,
        [NotNullWhen(true)] out WindowsClipboardSnapshot? snapshot)
    {
        ArgumentNullException.ThrowIfNull(text);
        snapshot = null;
        if (!WindowsOleScope.TryEnter(out WindowsOleScope? oleScope)) return false;

        ComDataObject? previous = null;
        bool scopeTransferred = false;
        try
        {
            if (!TryCapture(out previous, out uint sequenceBeforeCapture)) return false;

            bool written = TryWriteUnicodeText(
                text,
                sequenceBeforeCapture,
                out uint writtenSequenceNumber,
                out bool clipboardChanged);
            if (!written)
            {
                if (clipboardChanged
                    && RestoreDataObjectIfOwned(previous, writtenSequenceNumber)
                        != WindowsClipboardRestoreResult.Restored)
                    Logger.Warn("A escrita do clipboard falhou e o conteudo anterior nao foi restaurado.");
                return false;
            }

            snapshot = new WindowsClipboardSnapshot(previous, writtenSequenceNumber, oleScope!);
            previous = null;
            scopeTransferred = true;
            return true;
        }
        finally
        {
            if (previous != null && Marshal.IsComObject(previous)) Marshal.ReleaseComObject(previous);
            if (!scopeTransferred) oleScope?.Dispose();
        }
    }

    internal static WindowsClipboardRestoreResult RestoreIfOwned(WindowsClipboardSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return RestoreDataObjectIfOwned(snapshot.DataObject, snapshot.WrittenSequenceNumber);
    }

    private static WindowsClipboardRestoreResult RestoreDataObjectIfOwned(
        ComDataObject? dataObject,
        uint expectedSequenceNumber)
    {
        for (int attempt = 0; attempt < Attempts; attempt++)
        {
            if (WindowsNativeMethods.GetClipboardSequenceNumber() != expectedSequenceNumber)
                return WindowsClipboardRestoreResult.OwnershipLost;

            int result = dataObject == null
                ? EmptyClipboard(expectedSequenceNumber)
                : WindowsNativeMethods.OleSetClipboard(dataObject);
            if (result >= 0)
            {
                if (dataObject == null) return WindowsClipboardRestoreResult.Restored;
                int flushResult = WindowsNativeMethods.OleFlushClipboard();
                if (flushResult >= 0) return WindowsClipboardRestoreResult.Restored;
                LogHResult("Nao consegui materializar o clipboard restaurado", flushResult);
                return WindowsClipboardRestoreResult.Failed;
            }
            if (attempt + 1 < Attempts) Thread.Sleep(RetryDelayMilliseconds);
            else LogHResult("Nao consegui restaurar o clipboard anterior", result);
        }

        return WindowsClipboardRestoreResult.Failed;
    }

    private static bool TryCapture(
        out ComDataObject? dataObject,
        out uint sequenceNumber)
    {
        dataObject = null;
        sequenceNumber = 0;
        for (int attempt = 0; attempt < Attempts; attempt++)
        {
            sequenceNumber = WindowsNativeMethods.GetClipboardSequenceNumber();
            int result = WindowsNativeMethods.OleGetClipboard(out dataObject);
            if (result >= 0)
            {
                if (dataObject != null)
                {
                    if (WindowsNativeMethods.GetClipboardSequenceNumber() == sequenceNumber) return true;
                    Marshal.ReleaseComObject(dataObject);
                    dataObject = null;
                }
                else if (WindowsNativeMethods.GetClipboardSequenceNumber() == sequenceNumber)
                {
                    return true;
                }
            }
            else if (attempt + 1 == Attempts)
            {
                LogHResult("Nao consegui capturar o clipboard anterior", result);
                return false;
            }

            if (attempt + 1 < Attempts) Thread.Sleep(RetryDelayMilliseconds);
        }

        Logger.Warn("O clipboard mudou durante a captura; escrita recusada.");
        return false;
    }

    private static int EmptyClipboard(uint expectedSequenceNumber)
    {
        if (!WindowsClipboardOwner.TryCreate(out WindowsClipboardOwner? owner)) return -1;
        using (owner)
        {
            if (!WindowsNativeMethods.OpenClipboard(owner.Handle)) return -1;
            try
            {
                if (WindowsNativeMethods.GetClipboardSequenceNumber() != expectedSequenceNumber) return -1;
                return WindowsNativeMethods.EmptyClipboard() ? 0 : -1;
            }
            finally { WindowsNativeMethods.CloseClipboard(); }
        }
    }

    private static bool TryWriteUnicodeText(
        string text,
        uint expectedSequenceNumber,
        out uint writtenSequenceNumber,
        out bool clipboardChanged)
    {
        writtenSequenceNumber = expectedSequenceNumber;
        clipboardChanged = false;
        if (!WindowsClipboardOwner.TryCreate(out WindowsClipboardOwner? owner)) return false;
        using (owner)
        {
            int byteCount = checked((text.Length + 1) * sizeof(char));
            nint memory = WindowsNativeMethods.GlobalAlloc(
                WindowsNativeMethods.GmemMoveable,
                checked((nuint)byteCount));
            if (memory == nint.Zero)
            {
                Logger.Warn("GlobalAlloc falhou ao preparar o texto do clipboard.");
                return false;
            }

            try
            {
                nint destination = WindowsNativeMethods.GlobalLock(memory);
                if (destination == nint.Zero)
                {
                    Logger.Warn("GlobalLock falhou ao preparar o texto do clipboard.");
                    return false;
                }

                char[] characters = text.ToCharArray();
                if (characters.Length > 0) Marshal.Copy(characters, 0, destination, characters.Length);
                Marshal.WriteInt16(destination, text.Length * sizeof(char), 0);
                Marshal.SetLastPInvokeError(0);
                if (!WindowsNativeMethods.GlobalUnlock(memory) && Marshal.GetLastPInvokeError() != 0)
                {
                    Logger.Warn($"GlobalUnlock falhou (err {Marshal.GetLastPInvokeError()}).");
                    return false;
                }

                for (int attempt = 0; attempt < Attempts; attempt++)
                {
                    if (!WindowsNativeMethods.OpenClipboard(owner.Handle))
                    {
                        if (attempt + 1 < Attempts)
                        {
                            Thread.Sleep(RetryDelayMilliseconds);
                            continue;
                        }

                        Logger.Warn($"OpenClipboard falhou (err {Marshal.GetLastWin32Error()}).");
                        return false;
                    }

                    try
                    {
                        if (WindowsNativeMethods.GetClipboardSequenceNumber() != expectedSequenceNumber)
                        {
                            Logger.Warn("O clipboard mudou antes da escrita; escrita recusada.");
                            return false;
                        }

                        if (!WindowsNativeMethods.EmptyClipboard())
                        {
                            Logger.Warn($"EmptyClipboard falhou (err {Marshal.GetLastWin32Error()}).");
                            return false;
                        }

                        clipboardChanged = true;
                        writtenSequenceNumber = WindowsNativeMethods.GetClipboardSequenceNumber();
                        if (WindowsNativeMethods.SetClipboardData(
                                WindowsNativeMethods.CfUnicodeText,
                                memory) == nint.Zero)
                        {
                            writtenSequenceNumber = WindowsNativeMethods.GetClipboardSequenceNumber();
                            Logger.Warn($"SetClipboardData falhou (err {Marshal.GetLastWin32Error()}).");
                            return false;
                        }

                        memory = nint.Zero;
                        writtenSequenceNumber = WindowsNativeMethods.GetClipboardSequenceNumber();
                        return true;
                    }
                    finally
                    {
                        if (!WindowsNativeMethods.CloseClipboard())
                            Logger.Warn($"CloseClipboard falhou (err {Marshal.GetLastWin32Error()}).");
                    }
                }

                return false;
            }
            finally
            {
                if (memory != nint.Zero) WindowsNativeMethods.GlobalFree(memory);
            }
        }
    }

    private static void LogHResult(string message, int result)
    {
        string detail = Marshal.GetExceptionForHR(result)?.Message ?? $"HRESULT 0x{result:X8}";
        Logger.Warn(message + ": " + detail);
    }
}
