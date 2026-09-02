using System.Runtime.InteropServices;

namespace Matraca.Mac.Platform.Interop;

internal static class Frameworks
{
    private const string Foundation = "/System/Library/Frameworks/Foundation.framework/Foundation";
    private const string AppKit = "/System/Library/Frameworks/AppKit.framework/AppKit";
    private const string AudioToolbox = "/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

    private static readonly object Gate = new();
    private static bool _loaded;

    public static IntPtr CoreFoundationHandle { get; private set; }

    public static void EnsureLoaded()
    {
        lock (Gate)
        {
            if (_loaded) return;
            CoreFoundationHandle = Load(CoreFoundation);
            Load(Foundation);
            Load(AppKit);
            Load(AudioToolbox);
            Load(CoreGraphics);
            _loaded = true;
        }
    }

    public static IntPtr Symbol(IntPtr library, string name)
    {
        if (!NativeLibrary.TryGetExport(library, name, out IntPtr address)
            || address == IntPtr.Zero)
            throw new EntryPointNotFoundException($"Symbol not found: {name}");
        return Marshal.ReadIntPtr(address);
    }

    private static IntPtr Load(string path)
    {
        if (!NativeLibrary.TryLoad(path, out IntPtr handle))
            throw new DllNotFoundException($"Could not load framework: {path}");
        return handle;
    }
}
