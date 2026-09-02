using Matraca.Core;

namespace Matraca;

internal static class WindowsHotkeyTranslator
{
    private static readonly Dictionary<string, int> VirtualKeys = BuildVirtualKeys();

    public static int ToVirtualKey(HotkeyGesture gesture)
    {
        if (VirtualKeys.TryGetValue(gesture.Key, out var code)) return code;
        if (TryParseCode(gesture.Key, out code)) return code;
        throw new ArgumentException($"Tecla '{gesture.Key}' nao tem traducao Windows.", nameof(gesture));
    }

    public static HotkeyGesture? ParseCompatibility(string text)
    {
        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var modifiers = KeyMods.None;
        string? baseKey = null;

        foreach (var part in parts)
        {
            var modifier = part.ToLowerInvariant() switch
            {
                "ctrl" or "control" => KeyMods.Ctrl,
                "alt" => KeyMods.Alt,
                "shift" => KeyMods.Shift,
                "win" or "windows" => KeyMods.Win,
                _ => KeyMods.None,
            };
            if (modifier != KeyMods.None)
            {
                modifiers |= modifier;
                continue;
            }

            if (baseKey != null) return null;
            baseKey = part;
        }

        if (baseKey == null || !TryParseCode(baseKey, out var code) || code == 0) return null;
        var name = NameForVirtualKey(code);
        var namedGesture = new HotkeyGesture(name, modifiers);
        if (HotkeyParser.TryParse(namedGesture.ToString(), out var canonical)) return canonical;
        if (VirtualKeys.ContainsValue(code)) return null;

        return new HotkeyGesture(name, modifiers);
    }

    public static string Format(int virtualKey, KeyMods modifiers)
        => HotkeyParser.Format(new HotkeyGesture(NameForVirtualKey(virtualKey), modifiers));

    public static string NameForVirtualKey(int virtualKey)
    {
        foreach (var (name, code) in VirtualKeys)
            if (code == virtualKey) return name;
        return $"0x{virtualKey:X2}";
    }

    private static bool TryParseCode(string value, out int code)
    {
        try
        {
            code = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? Convert.ToInt32(value, 16)
                : int.Parse(value);
            return code > 0;
        }
        catch
        {
            code = 0;
            return false;
        }
    }

    private static Dictionary<string, int> BuildVirtualKeys()
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["F1"] = 0x70, ["F2"] = 0x71, ["F3"] = 0x72, ["F4"] = 0x73,
            ["F5"] = 0x74, ["F6"] = 0x75, ["F7"] = 0x76, ["F8"] = 0x77,
            ["F9"] = 0x78, ["F10"] = 0x79, ["F11"] = 0x7A, ["F12"] = 0x7B,
            ["F13"] = 0x7C, ["F14"] = 0x7D, ["F15"] = 0x7E, ["F16"] = 0x7F,
            ["F17"] = 0x80, ["F18"] = 0x81, ["F19"] = 0x82, ["F20"] = 0x83,
            ["F21"] = 0x84, ["F22"] = 0x85, ["F23"] = 0x86, ["F24"] = 0x87,
            ["Pause"] = 0x13, ["ScrollLock"] = 0x91, ["Apps"] = 0x5D,
            ["MediaPlayPause"] = 0xB3, ["MediaStop"] = 0xB2,
            ["MediaNext"] = 0xB0, ["MediaPrev"] = 0xB1,
            ["VolumeMute"] = 0xAD, ["VolumeDown"] = 0xAE, ["VolumeUp"] = 0xAF,
            ["LaunchApp1"] = 0xB6, ["LaunchApp2"] = 0xB7, ["LaunchMail"] = 0xB4,
            ["NumPad0"] = 0x60, ["NumPad1"] = 0x61, ["NumPad2"] = 0x62, ["NumPad3"] = 0x63,
            ["NumPad4"] = 0x64, ["NumPad5"] = 0x65, ["NumPad6"] = 0x66, ["NumPad7"] = 0x67,
            ["NumPad8"] = 0x68, ["NumPad9"] = 0x69,
            ["NumMultiply"] = 0x6A, ["NumAdd"] = 0x6B, ["NumSubtract"] = 0x6D,
            ["NumDecimal"] = 0x6E, ["NumDivide"] = 0x6F, ["NumLock"] = 0x90,
            ["Left"] = 0x25, ["Up"] = 0x26, ["Right"] = 0x27, ["Down"] = 0x28,
            ["Insert"] = 0x2D, ["Delete"] = 0x2E, ["Home"] = 0x24, ["End"] = 0x23,
            ["PageUp"] = 0x21, ["PageDown"] = 0x22, ["PrintScreen"] = 0x2C,
            ["Tab"] = 0x09, ["Space"] = 0x20, ["Enter"] = 0x0D, ["Backspace"] = 0x08,
            ["Esc"] = 0x1B, ["CapsLock"] = 0x14,
        };

        for (var code = 0x41; code <= 0x5A; code++) result[((char)code).ToString()] = code;
        for (var code = 0x30; code <= 0x39; code++) result[((char)code).ToString()] = code;
        return result;
    }
}
