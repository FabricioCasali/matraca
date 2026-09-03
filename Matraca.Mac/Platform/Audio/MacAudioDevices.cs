using Matraca.Core;
using Matraca.Mac.Platform.Interop;

namespace Matraca.Mac.Platform.Audio;

internal static class MacAudioDevices
{
    public static IReadOnlyList<MacAudioDevice> List()
    {
        if (!OperatingSystem.IsMacOS()) return Array.Empty<MacAudioDevice>();

        try
        {
            Frameworks.EnsureLoaded();
            return ReadDeviceIds()
                .Select(ReadInputDevice)
                .Where(device => device != null)
                .Cast<MacAudioDevice>()
                .OrderBy(device => device.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(device => device.Uid, StringComparer.Ordinal)
                .ToArray();
        }
        catch (Exception exception)
        {
            Logger.Warn($"Falha ao listar microfones do Mac: {exception.Message}");
            return Array.Empty<MacAudioDevice>();
        }
    }

    private static unsafe uint[] ReadDeviceIds()
    {
        var address = new AudioObjectPropertyAddress(
            CoreAudio.PropertyDevices,
            CoreAudio.ScopeGlobal,
            CoreAudio.ElementMain);
        uint size = GetPropertySize(CoreAudio.SystemObject, address, "lista de dispositivos");
        if (size % sizeof(uint) != 0)
            throw new InvalidOperationException("CoreAudio retornou uma lista de dispositivos invalida.");

        var ids = new uint[size / sizeof(uint)];
        if (ids.Length == 0) return ids;

        fixed (uint* data = ids)
        {
            int status = CoreAudio.AudioObjectGetPropertyData(
                CoreAudio.SystemObject,
                in address,
                0,
                IntPtr.Zero,
                ref size,
                (IntPtr)data);
            ThrowIfFailed("AudioObjectGetPropertyData(dispositivos)", status);
        }

        return ids;
    }

    private static MacAudioDevice? ReadInputDevice(uint id)
    {
        try
        {
            var streams = new AudioObjectPropertyAddress(
                CoreAudio.PropertyStreams,
                CoreAudio.ScopeInput,
                CoreAudio.ElementMain);
            if (GetPropertySize(id, streams, $"streams do dispositivo {id}") == 0)
                return null;

            var alive = new AudioObjectPropertyAddress(
                CoreAudio.PropertyDeviceIsAlive,
                CoreAudio.ScopeGlobal,
                CoreAudio.ElementMain);
            if (ReadUInt32(id, alive, "estado do dispositivo") == 0)
                return null;

            var name = new AudioObjectPropertyAddress(
                CoreAudio.PropertyName,
                CoreAudio.ScopeGlobal,
                CoreAudio.ElementMain);
            var uid = new AudioObjectPropertyAddress(
                CoreAudio.PropertyDeviceUid,
                CoreAudio.ScopeGlobal,
                CoreAudio.ElementMain);
            string deviceName = ReadString(id, name, "nome do dispositivo").Trim();
            string deviceUid = ReadString(id, uid, "UID do dispositivo");
            if (deviceName.Length == 0 || deviceUid.Length == 0)
                throw new InvalidOperationException($"O microfone {id} nao informou nome e UID.");

            return new MacAudioDevice(id, deviceName, deviceUid);
        }
        catch (Exception exception)
        {
            Logger.Warn($"Microfone CoreAudio {id} ilegivel: {exception.Message}");
            return null;
        }
    }

    private static uint GetPropertySize(
        uint objectId,
        AudioObjectPropertyAddress address,
        string property)
    {
        int status = CoreAudio.AudioObjectGetPropertyDataSize(
            objectId,
            in address,
            0,
            IntPtr.Zero,
            out uint size);
        ThrowIfFailed($"AudioObjectGetPropertyDataSize({property})", status);
        return size;
    }

    private static unsafe uint ReadUInt32(
        uint objectId,
        AudioObjectPropertyAddress address,
        string property)
    {
        uint value = 0;
        uint size = sizeof(uint);
        int status = CoreAudio.AudioObjectGetPropertyData(
            objectId,
            in address,
            0,
            IntPtr.Zero,
            ref size,
            (IntPtr)(&value));
        ThrowIfFailed($"AudioObjectGetPropertyData({property})", status);
        return value;
    }

    private static unsafe string ReadString(
        uint objectId,
        AudioObjectPropertyAddress address,
        string property)
    {
        IntPtr value = IntPtr.Zero;
        uint size = (uint)IntPtr.Size;
        int status = CoreAudio.AudioObjectGetPropertyData(
            objectId,
            in address,
            0,
            IntPtr.Zero,
            ref size,
            (IntPtr)(&value));
        ThrowIfFailed($"AudioObjectGetPropertyData({property})", status);
        if (value == IntPtr.Zero) return string.Empty;

        try { return CoreFoundation.GetString(value) ?? string.Empty; }
        finally { CoreFoundation.Release(value); }
    }

    private static void ThrowIfFailed(string operation, int status)
    {
        if (status != 0)
            throw new InvalidOperationException(
                $"{operation} falhou: {AudioToolbox.DescribeStatus(status)}");
    }
}
