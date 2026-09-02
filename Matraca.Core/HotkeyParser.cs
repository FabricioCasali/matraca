namespace Matraca.Core;

public static class HotkeyParser
{
    private const KeyMods AllModifiers = KeyMods.Ctrl | KeyMods.Alt | KeyMods.Shift | KeyMods.Win;

    private static readonly Dictionary<string, string> KeyNames = BuildKeyNames();

    public static HotkeyGesture Parse(string text)
    {
        if (TryParse(text, out var gesture)) return gesture;
        throw new FormatException($"Invalid hotkey gesture: '{text}'.");
    }

    public static bool TryParse(string? text, out HotkeyGesture gesture)
    {
        gesture = null!;
        var value = (text ?? "").Trim();
        if (value.Length == 0) return false;

        var parts = value.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var modifiers = KeyMods.None;
        string? key = null;

        foreach (var part in parts)
        {
            if (TryParseModifier(part, out var modifier))
            {
                modifiers |= modifier;
                continue;
            }

            if (key != null || !KeyNames.TryGetValue(part, out key)) return false;
        }

        if (key == null || modifiers == KeyMods.None && IsEssentialKey(key)) return false;
        gesture = new HotkeyGesture(key, modifiers);
        return true;
    }

    public static string Format(HotkeyGesture gesture)
    {
        ArgumentNullException.ThrowIfNull(gesture);
        if (string.IsNullOrWhiteSpace(gesture.Key))
            throw new ArgumentException("A hotkey must have a base key.", nameof(gesture));
        if ((gesture.Modifiers & ~AllModifiers) != 0)
            throw new ArgumentOutOfRangeException(nameof(gesture), "The hotkey has unknown modifiers.");

        var parts = new List<string>(5);
        if (gesture.Modifiers.HasFlag(KeyMods.Ctrl)) parts.Add("Ctrl");
        if (gesture.Modifiers.HasFlag(KeyMods.Alt)) parts.Add("Alt");
        if (gesture.Modifiers.HasFlag(KeyMods.Shift)) parts.Add("Shift");
        if (gesture.Modifiers.HasFlag(KeyMods.Win)) parts.Add("Win");
        parts.Add(gesture.Key);
        return string.Join('+', parts);
    }

    private static bool TryParseModifier(string value, out KeyMods modifier)
    {
        modifier = value.ToLowerInvariant() switch
        {
            "ctrl" or "control" => KeyMods.Ctrl,
            "alt" => KeyMods.Alt,
            "shift" => KeyMods.Shift,
            "win" or "windows" => KeyMods.Win,
            _ => KeyMods.None,
        };
        return modifier != KeyMods.None;
    }

    private static bool IsEssentialKey(string key)
        => key.Length == 1 && char.IsAsciiLetterOrDigit(key[0])
            || key is "Backspace" or "Tab" or "Enter" or "Esc" or "Space";

    private static Dictionary<string, string> BuildKeyNames()
    {
        var names = new[]
        {
            "Pause", "ScrollLock", "Apps",
            "MediaPlayPause", "MediaStop", "MediaNext", "MediaPrev",
            "VolumeMute", "VolumeDown", "VolumeUp",
            "LaunchApp1", "LaunchApp2", "LaunchMail",
            "NumMultiply", "NumAdd", "NumSubtract", "NumDecimal", "NumDivide", "NumLock",
            "Left", "Up", "Right", "Down", "Insert", "Delete", "Home", "End",
            "PageUp", "PageDown", "PrintScreen", "Tab", "Space", "Enter", "Backspace",
            "Esc", "CapsLock",
        };
        var result = names.ToDictionary(name => name, StringComparer.OrdinalIgnoreCase);
        for (var number = 1; number <= 24; number++) result[$"F{number}"] = $"F{number}";
        for (var number = 0; number <= 9; number++) result[$"NumPad{number}"] = $"NumPad{number}";
        for (var letter = 'A'; letter <= 'Z'; letter++) result[letter.ToString()] = letter.ToString();
        for (var digit = '0'; digit <= '9'; digit++) result[digit.ToString()] = digit.ToString();
        return result;
    }
}
