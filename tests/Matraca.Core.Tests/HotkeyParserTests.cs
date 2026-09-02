using Xunit;

namespace Matraca.Core.Tests;

public sealed class HotkeyParserTests
{
    [Theory]
    [InlineData("F15", "F15", KeyMods.None)]
    [InlineData("mediaplaypause", "MediaPlayPause", KeyMods.None)]
    [InlineData("Ctrl+Alt+X", "X", KeyMods.Ctrl | KeyMods.Alt)]
    [InlineData("shift+control+f24", "F24", KeyMods.Ctrl | KeyMods.Shift)]
    [InlineData("Windows+NumPad0", "NumPad0", KeyMods.Win)]
    [InlineData("NumPad0", "NumPad0", KeyMods.None)]
    public void ParsesAndCanonicalizes(string text, string key, KeyMods modifiers)
    {
        Assert.True(HotkeyParser.TryParse(text, out var gesture));
        Assert.Equal(key, gesture.Key);
        Assert.Equal(modifiers, gesture.Modifiers);
    }

    [Theory]
    [InlineData("Ctrl+Alt+X", "Ctrl+Alt+X")]
    [InlineData("shift+win+f13", "Shift+Win+F13")]
    [InlineData("control+windows+delete", "Ctrl+Win+Delete")]
    [InlineData("VolumeMute", "VolumeMute")]
    public void FormattingRoundTrips(string text, string canonical)
    {
        var gesture = HotkeyParser.Parse(text);

        Assert.Equal(canonical, gesture.ToString());
        Assert.Equal(gesture, HotkeyParser.Parse(gesture.ToString()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Ctrl+X+Y")]
    [InlineData("Hyper+F15")]
    [InlineData("NotAKey")]
    [InlineData("A")]
    [InlineData("7")]
    [InlineData("Space")]
    [InlineData("Esc")]
    [InlineData("0x7E")]
    [InlineData("126")]
    public void RejectsInvalidOrUnsafeGestures(string text)
        => Assert.False(HotkeyParser.TryParse(text, out _));

    [Fact]
    public void ParseThrowsForInvalidGesture()
        => Assert.Throws<FormatException>(() => HotkeyParser.Parse("Ctrl"));

    [Fact]
    public void FormatRejectsUnknownModifierBits()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => HotkeyParser.Format(new HotkeyGesture("F15", (KeyMods)16)));
}
