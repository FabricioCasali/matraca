using System.Runtime.InteropServices;
using System.Text;

namespace Matraca.Mac.Platform.Interop;

internal static unsafe class AudioToolbox
{
    public const uint PropertyIsRunning = 0x6171726E; // 'aqrn'
    public const uint PropertyCurrentDevice = 0x61716364; // 'aqcd'

    private const string Library =
        "/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox";

    [DllImport(Library)]
    public static extern int AudioQueueNewInput(
        ref AudioStreamBasicDescription format,
        delegate* unmanaged<IntPtr, IntPtr, AudioQueueBuffer*, IntPtr, uint, IntPtr, void> callback,
        IntPtr userData,
        IntPtr callbackRunLoop,
        IntPtr callbackRunLoopMode,
        uint flags,
        out IntPtr queue);

    [DllImport(Library)]
    public static extern int AudioQueueAllocateBuffer(
        IntPtr queue,
        uint bufferByteSize,
        out AudioQueueBuffer* buffer);

    [DllImport(Library)]
    public static extern int AudioQueueEnqueueBuffer(
        IntPtr queue,
        AudioQueueBuffer* buffer,
        uint packetDescriptionCount,
        IntPtr packetDescriptions);

    [DllImport(Library)]
    public static extern int AudioQueueStart(IntPtr queue, IntPtr startTime);

    [DllImport(Library)]
    public static extern int AudioQueueSetProperty(
        IntPtr queue,
        uint propertyId,
        IntPtr data,
        uint dataSize);

    [DllImport(Library)]
    public static extern int AudioQueueGetProperty(
        IntPtr queue,
        uint propertyId,
        out uint value,
        ref uint valueSize);

    [DllImport(Library)]
    public static extern int AudioQueueStop(
        IntPtr queue,
        [MarshalAs(UnmanagedType.I1)] bool immediate);

    [DllImport(Library)]
    public static extern int AudioQueueDispose(
        IntPtr queue,
        [MarshalAs(UnmanagedType.I1)] bool immediate);

    public static string DescribeStatus(int status)
    {
        if (status == 0) return "ok";

        byte[] bytes = BitConverter.GetBytes(status);
        Array.Reverse(bytes);
        return bytes.All(value => value is >= 0x20 and < 0x7F)
            ? $"{status} ('{Encoding.ASCII.GetString(bytes)}')"
            : status.ToString();
    }
}
