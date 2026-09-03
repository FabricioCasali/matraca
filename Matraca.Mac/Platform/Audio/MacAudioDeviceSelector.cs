namespace Matraca.Mac.Platform.Audio;

internal static class MacAudioDeviceSelector
{
    public static MacAudioDevice? Find(
        IReadOnlyList<MacAudioDevice> devices,
        string? configuredName)
    {
        string wanted = (configuredName ?? string.Empty).Trim();
        if (wanted.Length == 0) return null;

        return devices.FirstOrDefault(device =>
            string.Equals(device.Name, wanted, StringComparison.OrdinalIgnoreCase));
    }
}
