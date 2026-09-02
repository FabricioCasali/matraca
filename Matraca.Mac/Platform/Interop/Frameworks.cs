using System.Runtime.InteropServices;

namespace Matraca.Mac.Platform.Interop;

internal static class Frameworks
{
    private const string Foundation = "/System/Library/Frameworks/Foundation.framework/Foundation";
    private const string AppKit = "/System/Library/Frameworks/AppKit.framework/AppKit";

    private static readonly object Gate = new();
    private static bool _loaded;

    public static void EnsureLoaded()
    {
        lock (Gate)
        {
            if (_loaded) return;
            Load(Foundation);
            Load(AppKit);
            _loaded = true;
        }
    }

    private static void Load(string path)
    {
        if (!NativeLibrary.TryLoad(path, out _))
            throw new DllNotFoundException($"Could not load framework: {path}");
    }
}
