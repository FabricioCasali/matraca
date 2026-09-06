namespace Matraca.Core;

public sealed class RawConfig
{
    public string? modelPath { get; set; }
    public string? language { get; set; }
    public string? hotkey { get; set; }
    public string? pinHotkey { get; set; }
    public string? pinDelivery { get; set; }
    public string? mode { get; set; }
    public bool? autoEnter { get; set; }
    public bool? beep { get; set; }
    public float? beepVolume { get; set; }
    public string? startSound { get; set; }
    public string? stopSound { get; set; }
    public int? silenceMs { get; set; }
    public int? phraseMaxSeconds { get; set; }
    public float? vadThreshold { get; set; }
    public Dictionary<string, float>? micSensitivity { get; set; }
    public string? inputDevice { get; set; }
    public string[]? vocabulary { get; set; }
    public bool? history { get; set; }
    public int? historyMaxItems { get; set; }
    public bool? postProcess { get; set; }
    public string? postProcessProvider { get; set; }
    public string? postProcessEndpoint { get; set; }
    public string? postProcessModel { get; set; }
    public string? postProcessApiKey { get; set; }
    public string? postProcessOpenAiApiKey { get; set; }
    public string? postProcessDeepSeekApiKey { get; set; }
    public string? postProcessReasoning { get; set; }
    public string? postProcessPrompt { get; set; }
    public int? postProcessTimeoutMs { get; set; }
    public int? idleUnloadMinutes { get; set; }
    public string? gpu { get; set; }
    public bool? focusBorder { get; set; }
    public string? focusBorderColor { get; set; }
    public string? focusBorderColorBusy { get; set; }
    public string? focusBorderColorPinned { get; set; }
    public int? focusBorderThickness { get; set; }
    public float? focusBorderOpacity { get; set; }
    public string? pasteMethod { get; set; }
}
