using System.Runtime.InteropServices;
using System.Text;

namespace Matraca.Mac.Platform.Interop;

internal static class CoreFoundation
{
    private const string CoreFoundationFramework =
        "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const int NumberSInt64Type = 4;
    private const uint StringEncodingUtf8 = 0x08000100;

    [DllImport(CoreFoundationFramework)]
    private static extern IntPtr CFRetain(IntPtr value);

    [DllImport(CoreFoundationFramework)]
    private static extern void CFRelease(IntPtr value);

    [DllImport(CoreFoundationFramework)]
    private static extern nuint CFGetTypeID(IntPtr value);

    [DllImport(CoreFoundationFramework)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CFEqual(IntPtr first, IntPtr second);

    [DllImport(CoreFoundationFramework)]
    private static extern nuint CFStringGetTypeID();

    [DllImport(CoreFoundationFramework)]
    private static extern nint CFArrayGetCount(IntPtr array);

    [DllImport(CoreFoundationFramework)]
    private static extern IntPtr CFArrayGetValueAtIndex(IntPtr array, nint index);

    [DllImport(CoreFoundationFramework)]
    private static extern IntPtr CFDictionaryGetValue(IntPtr dictionary, IntPtr key);

    [DllImport(CoreFoundationFramework)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CFNumberGetValue(IntPtr number, int type, out long value);

    [DllImport(CoreFoundationFramework)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CFBooleanGetValue(IntPtr value);

    [DllImport(CoreFoundationFramework)]
    private static extern IntPtr CFStringCreateWithCString(
        IntPtr allocator,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string value,
        uint encoding);

    [DllImport(CoreFoundationFramework)]
    private static extern nint CFStringGetLength(IntPtr value);

    [DllImport(CoreFoundationFramework)]
    private static extern nint CFStringGetMaximumSizeForEncoding(nint length, uint encoding);

    [DllImport(CoreFoundationFramework)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CFStringGetCString(
        IntPtr value,
        byte[] buffer,
        nint bufferSize,
        uint encoding);

    public static IntPtr Retain(IntPtr value)
    {
        if (value == IntPtr.Zero)
            throw new ArgumentException("Cannot retain a null Core Foundation object.", nameof(value));
        return CFRetain(value);
    }

    public static void Release(IntPtr value)
    {
        if (value != IntPtr.Zero) CFRelease(value);
    }

    public static bool IsType(IntPtr value, nuint typeId)
        => value != IntPtr.Zero && CFGetTypeID(value) == typeId;

    public static bool AreEqual(IntPtr first, IntPtr second)
        => first != IntPtr.Zero && second != IntPtr.Zero && CFEqual(first, second);

    public static IntPtr CreateString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        IntPtr result = CFStringCreateWithCString(IntPtr.Zero, value, StringEncodingUtf8);
        if (result == IntPtr.Zero)
            throw new InvalidOperationException("CFStringCreateWithCString failed.");
        return result;
    }

    public static string? GetString(IntPtr value)
    {
        if (!IsType(value, CFStringGetTypeID())) return null;

        nint length = CFStringGetLength(value);
        nint maximumBytes = CFStringGetMaximumSizeForEncoding(length, StringEncodingUtf8);
        if (maximumBytes < 0 || maximumBytes >= int.MaxValue) return null;

        var buffer = new byte[(int)maximumBytes + 1];
        if (!CFStringGetCString(value, buffer, buffer.Length, StringEncodingUtf8)) return null;

        int terminator = Array.IndexOf(buffer, (byte)0);
        int byteCount = terminator >= 0 ? terminator : buffer.Length;
        return Encoding.UTF8.GetString(buffer, 0, byteCount);
    }

    public static nint GetArrayCount(IntPtr array)
        => array == IntPtr.Zero ? 0 : CFArrayGetCount(array);

    public static IntPtr GetArrayValue(IntPtr array, nint index)
        => CFArrayGetValueAtIndex(array, index);

    public static IntPtr GetDictionaryValue(IntPtr dictionary, IntPtr key)
        => CFDictionaryGetValue(dictionary, key);

    public static bool TryGetInt32(IntPtr number, out int value)
    {
        if (number != IntPtr.Zero
            && CFNumberGetValue(number, NumberSInt64Type, out long raw)
            && raw is >= int.MinValue and <= int.MaxValue)
        {
            value = (int)raw;
            return true;
        }

        value = 0;
        return false;
    }

    public static bool GetBoolean(IntPtr value)
        => value != IntPtr.Zero && CFBooleanGetValue(value);
}
