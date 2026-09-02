namespace Matraca.Core;

public sealed record HotkeyGesture(string Key, KeyMods Modifiers)
{
    public override string ToString() => HotkeyParser.Format(this);
}
