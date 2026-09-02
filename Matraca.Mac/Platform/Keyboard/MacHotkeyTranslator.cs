using Matraca.Core;

namespace Matraca.Mac.Platform.Keyboard;

internal static class MacHotkeyTranslator
{
    private static readonly Dictionary<string, ushort> KeyCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["A"] = 0x00, ["S"] = 0x01, ["D"] = 0x02, ["F"] = 0x03,
        ["H"] = 0x04, ["G"] = 0x05, ["Z"] = 0x06, ["X"] = 0x07,
        ["C"] = 0x08, ["V"] = 0x09, ["B"] = 0x0B, ["Q"] = 0x0C,
        ["W"] = 0x0D, ["E"] = 0x0E, ["R"] = 0x0F, ["Y"] = 0x10,
        ["T"] = 0x11, ["1"] = 0x12, ["2"] = 0x13, ["3"] = 0x14,
        ["4"] = 0x15, ["6"] = 0x16, ["5"] = 0x17, ["="] = 0x18,
        ["9"] = 0x19, ["7"] = 0x1A, ["-"] = 0x1B, ["8"] = 0x1C,
        ["0"] = 0x1D, ["]"] = 0x1E, ["O"] = 0x1F, ["U"] = 0x20,
        ["["] = 0x21, ["I"] = 0x22, ["P"] = 0x23, ["L"] = 0x25,
        ["J"] = 0x26, ["'"] = 0x27, ["K"] = 0x28, [";"] = 0x29,
        ["\\"] = 0x2A, [","] = 0x2B, ["/"] = 0x2C, ["N"] = 0x2D,
        ["M"] = 0x2E, ["."] = 0x2F, ["SPACE"] = 0x31,
        ["ENTER"] = 0x24, ["TAB"] = 0x30, ["ESC"] = 0x35,
        ["F1"] = 0x7A, ["F2"] = 0x78, ["F3"] = 0x63, ["F4"] = 0x76,
        ["F5"] = 0x60, ["F6"] = 0x61, ["F7"] = 0x62, ["F8"] = 0x64,
        ["F9"] = 0x65, ["F10"] = 0x6D, ["F11"] = 0x67, ["F12"] = 0x6F,
        ["F13"] = 0x69, ["F14"] = 0x6B, ["F15"] = 0x71, ["F16"] = 0x6A,
        ["F17"] = 0x40, ["F18"] = 0x4F, ["F19"] = 0x50, ["F20"] = 0x5A,
    };

    private static readonly Dictionary<ushort, string> Names =
        KeyCodes.ToDictionary(pair => pair.Value, pair => pair.Key);

    public static ushort ToKeyCode(HotkeyGesture gesture)
        => KeyCodes.TryGetValue(gesture.Key, out ushort keyCode)
            ? keyCode
            : throw new ArgumentException($"Unsupported macOS key: {gesture.Key}", nameof(gesture));

    public static string NameForKeyCode(ushort keyCode)
        => Names.TryGetValue(keyCode, out string? name) ? name : $"KeyCode{keyCode}";
}
