using System.Runtime.InteropServices;

namespace Matraca.Mac.Platform.Interop;

internal static class CoreAudio
{
    public const uint SystemObject = 1;
    public const uint PropertyDevices = 0x64657623; // 'dev#'
    public const uint PropertyName = 0x6C6E616D; // 'lnam'
    public const uint PropertyDeviceUid = 0x75696420; // 'uid '
    public const uint PropertyDeviceIsAlive = 0x6C69766E; // 'livn'
    public const uint PropertyStreams = 0x73746D23; // 'stm#'
    public const uint ScopeGlobal = 0x676C6F62; // 'glob'
    public const uint ScopeInput = 0x696E7074; // 'inpt'
    public const uint ElementMain = 0;

    private const string Library =
        "/System/Library/Frameworks/CoreAudio.framework/CoreAudio";

    [DllImport(Library)]
    public static extern int AudioObjectGetPropertyDataSize(
        uint objectId,
        in AudioObjectPropertyAddress address,
        uint qualifierDataSize,
        IntPtr qualifierData,
        out uint dataSize);

    [DllImport(Library)]
    public static extern int AudioObjectGetPropertyData(
        uint objectId,
        in AudioObjectPropertyAddress address,
        uint qualifierDataSize,
        IntPtr qualifierData,
        ref uint dataSize,
        IntPtr data);
}
