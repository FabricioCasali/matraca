using Matraca;
using Xunit;

namespace Matraca.Core.Tests;

public sealed class WindowsHotkeyTranslatorTests
{
    [Theory]
    [InlineData("F15", 0x7E)]
    [InlineData("MediaPlayPause", 0xB3)]
    [InlineData("LaunchApp1", 0xB6)]
    [InlineData("NumPad0", 0x60)]
    [InlineData("X", 0x58)]
    public void CanonicalNamesTranslateToWindowsVirtualKeys(string key, int expected)
        => Assert.Equal(expected, WindowsHotkeyTranslator.ToVirtualKey(new HotkeyGesture(key, KeyMods.Ctrl)));

    [Theory]
    [InlineData("0xB6", "LaunchApp1", KeyMods.None)]
    [InlineData("182", "LaunchApp1", KeyMods.None)]
    [InlineData("Ctrl+0x58", "X", KeyMods.Ctrl)]
    [InlineData("control+windows+0x7E", "F15", KeyMods.Ctrl | KeyMods.Win)]
    [InlineData("Alt+0xE8", "0xE8", KeyMods.Alt)]
    public void LegacyWindowsCodesRemainAccepted(string text, string key, KeyMods modifiers)
    {
        var gesture = WindowsHotkeyTranslator.ParseCompatibility(text);

        Assert.NotNull(gesture);
        Assert.Equal(key, gesture.Key);
        Assert.Equal(modifiers, gesture.Modifiers);
        Assert.True(WindowsHotkeyTranslator.ToVirtualKey(gesture) > 0);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("0xZZ")]
    [InlineData("0x41")]
    [InlineData("Ctrl+0x58+0x59")]
    public void InvalidOrUnsafeLegacyCodesAreRejected(string text)
        => Assert.Null(WindowsHotkeyTranslator.ParseCompatibility(text));

    [Fact]
    public void CapturedUnknownCodeFormatsAndRoundTrips()
    {
        var text = WindowsHotkeyTranslator.Format(0xE8, KeyMods.Ctrl | KeyMods.Shift);
        var gesture = WindowsHotkeyTranslator.ParseCompatibility(text);

        Assert.Equal("Ctrl+Shift+0xE8", text);
        Assert.NotNull(gesture);
        Assert.Equal(0xE8, WindowsHotkeyTranslator.ToVirtualKey(gesture));
    }
}
