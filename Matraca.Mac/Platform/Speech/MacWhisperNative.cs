using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Matraca.Core;

namespace Matraca.Mac.Platform.Speech;

internal static partial class MacWhisperNative
{
    private const string Library = "libmatraca-whisper";
    private static int _backendInitializations;
    private static int _backendReleases;

    internal static int BackendInitializations => Volatile.Read(ref _backendInitializations);
    internal static int BackendReleases => Volatile.Read(ref _backendReleases);

    internal static unsafe IntPtr LoggerCallback
        => (IntPtr)(delegate* unmanaged[Cdecl]<int, IntPtr, void>)&OnNativeLog;

    internal static void ResetCounters()
    {
        Volatile.Write(ref _backendInitializations, 0);
        Volatile.Write(ref _backendReleases, 0);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnNativeLog(int level, IntPtr messagePointer)
    {
        string message = Marshal.PtrToStringUTF8(messagePointer)?.Trim() ?? "";
        if (message.Length == 0) return;
        if (message.Contains("whisper_backend_init_gpu: using", StringComparison.Ordinal))
            Interlocked.Increment(ref _backendInitializations);
        if (message.Contains("ggml_metal_free: deallocating", StringComparison.Ordinal))
            Interlocked.Increment(ref _backendReleases);
        Logger.Info($"[whisper.cpp/{level}] {message}");
    }

    [LibraryImport(Library, EntryPoint = "matraca_whisper_open", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int Open(
        string runtimeDirectory,
        string modelPath,
        string language,
        string prompt,
        int useGpu,
        IntPtr log,
        out IntPtr session,
        out IntPtr error);

    [LibraryImport(Library, EntryPoint = "matraca_whisper_transcribe")]
    internal static partial int Transcribe(
        IntPtr session,
        float[] samples,
        int sampleCount,
        out IntPtr text,
        out IntPtr error);

    [LibraryImport(Library, EntryPoint = "matraca_whisper_cancel")]
    internal static partial void Cancel(IntPtr session);

    [LibraryImport(Library, EntryPoint = "matraca_whisper_reset_cancel")]
    internal static partial void ResetCancel(IntPtr session);

    [LibraryImport(Library, EntryPoint = "matraca_whisper_run_count")]
    internal static partial ulong RunCount(IntPtr session);

    [LibraryImport(Library, EntryPoint = "matraca_whisper_string_free")]
    internal static partial void FreeString(IntPtr value);

    [LibraryImport(Library, EntryPoint = "matraca_whisper_close")]
    internal static partial void Close(IntPtr session);
}
