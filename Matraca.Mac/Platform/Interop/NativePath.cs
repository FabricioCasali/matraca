using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Matraca.Mac.Platform.Interop;

internal static class NativePath
{
    private const string Library = "/usr/lib/libSystem.B.dylib";
    private const int PathBufferSize = 4096;

    [DllImport(Library, CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr realpath(string path, byte[] resolvedPath);

    public static string Resolve(string path)
    {
        var buffer = new byte[PathBufferSize];
        if (realpath(path, buffer) == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastPInvokeError(), $"Could not resolve path: {path}");

        int length = Array.IndexOf(buffer, (byte)0);
        if (length < 0)
            throw new PathTooLongException($"Resolved path exceeds {PathBufferSize} bytes: {path}");
        return System.Text.Encoding.UTF8.GetString(buffer, 0, length);
    }
}
