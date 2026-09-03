using Matraca.Mac.Platform.Audio;
using Xunit;

namespace Matraca.Core.Tests;

public sealed class MacAudioDeviceSelectorTests
{
    private static readonly MacAudioDevice BuiltIn =
        new(12, "MacBook Pro Microphone", "BuiltInMicrophoneDevice");
    private static readonly MacAudioDevice Studio =
        new(42, "Studio Mic", "AppleUSBAudioEngine:StudioMic");

    [Fact]
    public void EmptyConfigurationTracksSystemDefault()
        => Assert.Null(MacAudioDeviceSelector.Find([BuiltIn, Studio], "  "));

    [Fact]
    public void ConfiguredNameMatchesWithoutCaseOrOuterWhitespace()
        => Assert.Same(
            Studio,
            MacAudioDeviceSelector.Find([BuiltIn, Studio], "  studio mic "));

    [Fact]
    public void MissingConfiguredDeviceFallsBackToDefault()
        => Assert.Null(MacAudioDeviceSelector.Find([BuiltIn], "Studio Mic"));

    [Fact]
    public void RepeatedSelectionUsesTheCurrentEnumeration()
    {
        Assert.Same(Studio, MacAudioDeviceSelector.Find([BuiltIn, Studio], "Studio Mic"));
        Assert.Null(MacAudioDeviceSelector.Find([BuiltIn], "Studio Mic"));
    }
}
