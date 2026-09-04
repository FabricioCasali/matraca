using Xunit;

namespace Matraca.Core.Tests;

public sealed class ConfigTests
{
    [Fact]
    public void EmptyRawUsesSafeFallbacksAlignedWithPackagedBehavior()
    {
        var config = Config.FromRaw(new RawConfig());

        Assert.Equal("", config.ModelPath);
        Assert.Equal("pt", config.Language);
        Assert.False(config.DiscoverMode);
        Assert.Equal(new HotkeyGesture("F15", KeyMods.None), config.Hotkey);
        Assert.Null(config.PinHotkey);
        Assert.Equal("live", config.Mode);
        Assert.Equal("focus", config.PinDelivery);
        Assert.Equal("unicode", config.PasteMethod);
        Assert.True(config.Beep);
        Assert.True(config.History);
        Assert.Equal("auto", config.Gpu);
        Assert.Equal("anthropic", config.PostProcessProvider);
        Assert.Equal("", config.PostProcessEndpoint);
    }

    [Theory]
    [InlineData("anthropic", "anthropic")]
    [InlineData("openai", "openai-compatible")]
    [InlineData("openai-compatible", "openai-compatible")]
    [InlineData("unknown", "anthropic")]
    public void PostProcessProviderUsesCanonicalValues(string raw, string expected)
    {
        var config = Config.FromRaw(new RawConfig { postProcessProvider = raw });

        Assert.Equal(expected, config.PostProcessProvider);
    }

    [Theory]
    [InlineData("toggle", false)]
    [InlineData("live", false)]
    [InlineData("hold", true)]
    [InlineData("push", true)]
    public void ModeIdentifiesWhetherHookNeedsKeyUp(string mode, bool needsKeyUp)
    {
        var config = Config.FromRaw(new RawConfig { mode = mode });

        Assert.Equal(needsKeyUp, config.HotkeyNeedsKeyUp);
    }

    [Fact]
    public void InvalidValuesFallBackOrClampToUiRanges()
    {
        var config = Config.FromRaw(new RawConfig
        {
            mode = "unknown",
            pinDelivery = "unknown",
            pasteMethod = "unknown",
            gpu = "unknown",
            beepVolume = 4,
            silenceMs = 10,
            phraseMaxSeconds = 100,
            vadThreshold = 0,
            historyMaxItems = 9000,
            postProcessTimeoutMs = 50,
            idleUnloadMinutes = -1,
            focusBorderThickness = 80,
            focusBorderOpacity = 0,
        });

        Assert.Equal("live", config.Mode);
        Assert.Equal("focus", config.PinDelivery);
        Assert.Equal("unicode", config.PasteMethod);
        Assert.Equal("auto", config.Gpu);
        Assert.Equal(1, config.BeepVolume);
        Assert.Equal(200, config.SilenceMs);
        Assert.Equal(20, config.PhraseMaxSeconds);
        Assert.Equal(0.001f, config.VadThreshold);
        Assert.Equal(5000, config.HistoryMaxItems);
        Assert.Equal(1000, config.PostProcessTimeoutMs);
        Assert.Equal(0, config.IdleUnloadMinutes);
        Assert.Equal(40, config.FocusBorderThickness);
        Assert.Equal(0.1f, config.FocusBorderOpacity);
    }

    [Theory]
    [InlineData("gpu", "gpu")]
    [InlineData("vulkan", "gpu")]
    [InlineData("cpu", "cpu")]
    [InlineData("auto", "auto")]
    public void AccelerationUsesCanonicalValuesAndAcceptsLegacyAlias(string raw, string expected)
    {
        var config = Config.FromRaw(new RawConfig { gpu = raw });

        Assert.Equal(expected, config.Gpu);
    }

    [Fact]
    public void VocabularyAndMicrophoneSensitivityAreNormalized()
    {
        var config = Config.FromRaw(new RawConfig
        {
            inputDevice = "  Studio Mic  ",
            vocabulary = [" Matraca ", "", "matraca", "Whisper"],
            vadThreshold = 0.02f,
            micSensitivity = new Dictionary<string, float>
            {
                [" Studio Mic "] = 1,
                [""] = 0.1f,
                ["Invalid"] = 0,
            },
        });

        Assert.Equal("Studio Mic", config.InputDevice);
        Assert.Equal(["Matraca", "Whisper"], config.Vocabulary);
        Assert.Equal(0.5f, config.MicSensitivity["studio mic"]);
        Assert.Equal(0.5f, config.EffectiveVadThreshold);
        Assert.Equal(0.02f, config.VadThresholdFor("another mic"));
    }

    [Fact]
    public void InvalidDictationHotkeyEnablesDiscovery()
    {
        var warnings = new List<string>();
        var config = Config.FromRaw(new RawConfig { hotkey = "A" }, warning: warnings.Add);

        Assert.True(config.DiscoverMode);
        Assert.Null(config.Hotkey);
        Assert.Equal("discover", config.HotkeyName);
        Assert.Single(warnings);
    }

    [Fact]
    public void InvalidOrCollidingPinHotkeyIsDisabled()
    {
        var invalid = Config.FromRaw(new RawConfig { hotkey = "F15", pinHotkey = "A" });
        var duplicate = Config.FromRaw(new RawConfig { hotkey = "Ctrl+F15", pinHotkey = "control+f15" });

        Assert.Null(invalid.PinHotkey);
        Assert.Null(duplicate.PinHotkey);
    }

    [Fact]
    public void CompatibilityParserCanCanonicalizePlatformAliases()
    {
        var config = Config.FromRaw(
            new RawConfig { hotkey = "0xB6" },
            value => value == "0xB6" ? new HotkeyGesture("LaunchApp1", KeyMods.None) : null);

        Assert.False(config.DiscoverMode);
        Assert.Equal("LaunchApp1", config.HotkeyName);
    }
}
