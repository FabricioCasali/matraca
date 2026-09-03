using System.Runtime.InteropServices;

namespace Matraca.Mac.Platform.Interop;

internal static unsafe class AVAudioPlayerInterop
{
    private const string AVFAudioFramework =
        "/System/Library/Frameworks/AVFAudio.framework/AVFAudio";
    private const string ObjCLibrary = "/usr/lib/libobjc.A.dylib";

    private static readonly IntPtr NSData;
    private static readonly IntPtr AVAudioPlayer;
    private static readonly IntPtr DataWithBytesLength;
    private static readonly IntPtr InitWithDataError;
    private static readonly IntPtr SetVolume;
    private static readonly IntPtr Duration;
    private static readonly IntPtr Play;
    private static readonly IntPtr IsPlayingSelector;
    private static readonly IntPtr Stop;
    private static readonly IntPtr LocalizedDescription;

    static AVAudioPlayerInterop()
    {
        Frameworks.EnsureLoaded();
        if (!NativeLibrary.TryLoad(AVFAudioFramework, out _))
            throw new DllNotFoundException($"Could not load framework: {AVFAudioFramework}");

        NSData = GetClass("NSData");
        AVAudioPlayer = GetClass("AVAudioPlayer");
        DataWithBytesLength = ObjC.sel_registerName("dataWithBytes:length:");
        InitWithDataError = ObjC.sel_registerName("initWithData:error:");
        SetVolume = ObjC.sel_registerName("setVolume:");
        Duration = ObjC.sel_registerName("duration");
        Play = ObjC.sel_registerName("play");
        IsPlayingSelector = ObjC.sel_registerName("isPlaying");
        Stop = ObjC.sel_registerName("stop");
        LocalizedDescription = ObjC.sel_registerName("localizedDescription");
    }

    public static bool TryStart(
        byte[] audioFile,
        float volume,
        int maximumDurationMilliseconds,
        out IntPtr player,
        out int durationMilliseconds,
        out string? error)
    {
        player = IntPtr.Zero;
        durationMilliseconds = 0;
        error = null;
        if (audioFile.Length == 0)
        {
            error = "o arquivo de audio esta vazio";
            return false;
        }

        using var pool = AutoreleasePool.New();
        IntPtr data;
        fixed (byte* bytes = audioFile)
            data = SendData(NSData, DataWithBytesLength, bytes, (nuint)audioFile.Length);
        if (data == IntPtr.Zero)
        {
            error = "NSData nao aceitou o arquivo de audio";
            return false;
        }

        IntPtr nativeError;
        IntPtr allocated = ObjC.Send(AVAudioPlayer, ObjCSelectors.Alloc);
        IntPtr initialized = SendInitWithError(
            allocated,
            InitWithDataError,
            data,
            out nativeError);
        if (initialized == IntPtr.Zero)
        {
            error = DescribeError(nativeError) ?? "AVAudioPlayer nao reconheceu o arquivo";
            return false;
        }

        double durationSeconds = SendDouble(initialized, Duration);
        if (!double.IsFinite(durationSeconds) || durationSeconds <= 0)
        {
            ObjC.SendVoid(initialized, ObjCSelectors.Release);
            error = "AVAudioPlayer devolveu uma duracao invalida";
            return false;
        }

        double duration = Math.Ceiling(durationSeconds * 1000);
        if (duration > maximumDurationMilliseconds)
        {
            ObjC.SendVoid(initialized, ObjCSelectors.Release);
            error = $"duracao de {duration / 1000:F1}s excede o limite de "
                + $"{maximumDurationMilliseconds / 1000}s para feedback sonoro";
            return false;
        }

        SendVoidFloat(initialized, SetVolume, volume);
        if (!SendBool(initialized, Play))
        {
            ObjC.SendVoid(initialized, ObjCSelectors.Release);
            error = "AVAudioPlayer recusou a reproducao";
            return false;
        }

        player = initialized;
        durationMilliseconds = Math.Max(1, (int)duration);
        return true;
    }

    public static bool IsPlaying(IntPtr player)
    {
        if (player == IntPtr.Zero) return false;
        using var pool = AutoreleasePool.New();
        return SendBool(player, IsPlayingSelector);
    }

    public static void StopAndRelease(IntPtr player)
    {
        if (player == IntPtr.Zero) return;
        using var pool = AutoreleasePool.New();
        try
        {
            if (SendBool(player, IsPlayingSelector)) ObjC.SendVoid(player, Stop);
        }
        finally
        {
            ObjC.SendVoid(player, ObjCSelectors.Release);
        }
    }

    private static IntPtr GetClass(string name)
    {
        IntPtr value = ObjC.objc_getClass(name);
        return value != IntPtr.Zero
            ? value
            : throw new TypeLoadException($"Objective-C class not found: {name}");
    }

    private static string? DescribeError(IntPtr error)
    {
        if (error == IntPtr.Zero) return null;
        IntPtr description = ObjC.Send(error, LocalizedDescription);
        return NSStringRef.To(description);
    }

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendData(
        IntPtr receiver,
        IntPtr selector,
        byte* bytes,
        nuint length);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendInitWithError(
        IntPtr receiver,
        IntPtr selector,
        IntPtr data,
        out IntPtr error);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    private static extern void SendVoidFloat(IntPtr receiver, IntPtr selector, float value);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    private static extern double SendDouble(IntPtr receiver, IntPtr selector);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SendBool(IntPtr receiver, IntPtr selector);
}
