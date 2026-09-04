using System.Text.Json;
using System.Text.Json.Serialization;

namespace Matraca.Core;

public sealed class Config
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string ModelPath { get; init; } = "";
    public string Language { get; init; } = "pt";
    public bool DiscoverMode { get; init; }
    public HotkeyGesture? Hotkey { get; init; } = new("F15", KeyMods.None);
    public string HotkeyName => DiscoverMode ? "discover" : Hotkey?.ToString() ?? "discover";
    public HotkeyGesture? PinHotkey { get; init; }
    public string PinHotkeyName => PinHotkey?.ToString() ?? "";
    public string PinDelivery { get; init; } = "focus";
    public string Mode { get; init; } = "live";
    public bool HotkeyNeedsKeyUp => Mode is "hold" or "push";
    public bool AutoEnter { get; init; }
    public bool Beep { get; init; } = true;
    public float BeepVolume { get; init; } = 0.8f;
    public string StartSound { get; init; } = "";
    public string StopSound { get; init; } = "";
    public int SilenceMs { get; init; } = 450;
    public float VadThreshold { get; init; } = 0.012f;
    public Dictionary<string, float> MicSensitivity { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public int PhraseMaxSeconds { get; init; } = 6;
    public string InputDevice { get; init; } = "";
    public string[] Vocabulary { get; init; } = Array.Empty<string>();
    public bool History { get; init; } = true;
    public int HistoryMaxItems { get; init; } = 100;
    public bool PostProcess { get; init; }
    public string PostProcessProvider { get; init; } = "anthropic";
    public string PostProcessEndpoint { get; init; } = "";
    public string PostProcessModel { get; init; } = "claude-opus-5";
    public string PostProcessApiKey { get; init; } = "";
    public string PostProcessPrompt { get; init; } = "";
    public int PostProcessTimeoutMs { get; init; } = 8000;
    public int IdleUnloadMinutes { get; init; } = 5;
    public string Gpu { get; init; } = "auto";
    public bool FocusBorder { get; init; } = true;
    public string FocusBorderColor { get; init; } = "#E81123";
    public string FocusBorderColorBusy { get; init; } = "#FFB900";
    public string FocusBorderColorPinned { get; init; } = "#0078D4";
    public int FocusBorderThickness { get; init; } = 4;
    public float FocusBorderOpacity { get; init; } = 0.9f;
    public string PasteMethod { get; init; } = "unicode";

    public float EffectiveVadThreshold => VadThresholdFor(InputDevice);

    public float VadThresholdFor(string? device)
    {
        var name = (device ?? "").Trim();
        return name.Length > 0 && MicSensitivity.TryGetValue(name, out var value) && value > 0
            ? value
            : VadThreshold;
    }

    public Config WithGpu(string gpu) => new()
    {
        ModelPath = ModelPath,
        Language = Language,
        DiscoverMode = DiscoverMode,
        Hotkey = Hotkey,
        PinHotkey = PinHotkey,
        PinDelivery = PinDelivery,
        Mode = Mode,
        AutoEnter = AutoEnter,
        Beep = Beep,
        BeepVolume = BeepVolume,
        StartSound = StartSound,
        StopSound = StopSound,
        SilenceMs = SilenceMs,
        VadThreshold = VadThreshold,
        MicSensitivity = MicSensitivity,
        PhraseMaxSeconds = PhraseMaxSeconds,
        InputDevice = InputDevice,
        Vocabulary = Vocabulary,
        History = History,
        HistoryMaxItems = HistoryMaxItems,
        PostProcess = PostProcess,
        PostProcessProvider = PostProcessProvider,
        PostProcessEndpoint = PostProcessEndpoint,
        PostProcessModel = PostProcessModel,
        PostProcessApiKey = PostProcessApiKey,
        PostProcessPrompt = PostProcessPrompt,
        PostProcessTimeoutMs = PostProcessTimeoutMs,
        IdleUnloadMinutes = IdleUnloadMinutes,
        Gpu = gpu,
        FocusBorder = FocusBorder,
        FocusBorderColor = FocusBorderColor,
        FocusBorderColorBusy = FocusBorderColorBusy,
        FocusBorderColorPinned = FocusBorderColorPinned,
        FocusBorderThickness = FocusBorderThickness,
        FocusBorderOpacity = FocusBorderOpacity,
        PasteMethod = PasteMethod,
    };

    public static Config Load(
        AppPaths paths,
        string packagedConfigFile,
        Func<string, HotkeyGesture?>? compatibilityParser = null,
        Action<string>? warning = null)
        => FromRaw(LoadRaw(paths, packagedConfigFile, warning), compatibilityParser, warning, paths);

    public static RawConfig LoadRaw(AppPaths paths, string packagedConfigFile, Action<string>? warning = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var path = File.Exists(paths.ConfigFile) ? paths.ConfigFile : packagedConfigFile;
        if (!File.Exists(path)) return new RawConfig();

        try
        {
            return JsonSerializer.Deserialize<RawConfig>(File.ReadAllText(path), ReadOptions)
                ?? new RawConfig();
        }
        catch (Exception ex)
        {
            warning?.Invoke($"Falha ao ler appsettings.json: {ex.Message}");
            return new RawConfig();
        }
    }

    public static void SaveRaw(AppPaths paths, RawConfig raw)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(raw);
        Directory.CreateDirectory(paths.DataDirectory);
        string temporaryPath = Path.Combine(
            paths.DataDirectory,
            $".{Path.GetFileName(paths.ConfigFile)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, raw, WriteOptions);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, paths.ConfigFile, overwrite: true);
            temporaryPath = "";
        }
        finally
        {
            if (temporaryPath.Length > 0)
            {
                try { File.Delete(temporaryPath); } catch { }
            }
        }
    }

    public static Config FromRaw(
        RawConfig raw,
        Func<string, HotkeyGesture?>? compatibilityParser = null,
        Action<string>? warning = null,
        AppPaths? paths = null)
    {
        ArgumentNullException.ThrowIfNull(raw);

        var hotkeyText = raw.hotkey?.Trim() ?? "F15";
        var discover = hotkeyText.Equals("discover", StringComparison.OrdinalIgnoreCase);
        HotkeyGesture? hotkey = discover ? null : ResolveHotkey(hotkeyText, compatibilityParser);
        if (!discover && hotkey == null)
        {
            warning?.Invoke($"Atalho '{hotkeyText}' invalido; caindo em modo descoberta.");
            discover = true;
        }

        HotkeyGesture? pinHotkey = null;
        var pinText = (raw.pinHotkey ?? "").Trim();
        if (pinText.Length > 0 && !pinText.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            pinHotkey = ResolveHotkey(pinText, compatibilityParser);
            if (pinHotkey == null)
                warning?.Invoke($"pinHotkey '{pinText}' nao reconhecida; fixar janela ficou desligado.");
            else if (pinHotkey == hotkey)
            {
                warning?.Invoke($"pinHotkey '{pinText}' e o mesmo atalho do ditado; fixar janela ficou desligado.");
                pinHotkey = null;
            }
        }

        string postProcessProvider = NormalizePostProcessProvider(raw.postProcessProvider);

        return new Config
        {
            ModelPath = ResolveConfiguredPath(raw.modelPath, paths),
            Language = string.IsNullOrWhiteSpace(raw.language) ? "pt" : raw.language.Trim(),
            DiscoverMode = discover,
            Hotkey = hotkey,
            PinHotkey = pinHotkey,
            PinDelivery = EqualsValue(raw.pinDelivery, "nofocus") ? "nofocus" : "focus",
            Mode = NormalizeMode(raw.mode),
            AutoEnter = raw.autoEnter ?? false,
            Beep = raw.beep ?? true,
            BeepVolume = Math.Clamp(raw.beepVolume ?? 0.8f, 0f, 1f),
            StartSound = ResolveConfiguredPath(raw.startSound, paths),
            StopSound = ResolveConfiguredPath(raw.stopSound, paths),
            SilenceMs = Math.Clamp(raw.silenceMs ?? 450, 200, 5000),
            PhraseMaxSeconds = Math.Clamp(raw.phraseMaxSeconds ?? 6, 2, 20),
            VadThreshold = Math.Clamp(raw.vadThreshold ?? 0.012f, 0.001f, 0.5f),
            MicSensitivity = BuildMicSensitivity(raw.micSensitivity),
            InputDevice = (raw.inputDevice ?? "").Trim(),
            Vocabulary = (raw.vocabulary ?? Array.Empty<string>())
                .Select(value => (value ?? "").Trim())
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            History = raw.history ?? true,
            HistoryMaxItems = Math.Clamp(raw.historyMaxItems ?? 100, 1, 5000),
            PostProcess = raw.postProcess ?? false,
            PostProcessProvider = postProcessProvider,
            PostProcessEndpoint = (raw.postProcessEndpoint ?? "").Trim(),
            PostProcessModel = string.IsNullOrWhiteSpace(raw.postProcessModel)
                ? "claude-opus-5"
                : raw.postProcessModel.Trim(),
            PostProcessApiKey = ((postProcessProvider == "openai-compatible"
                ? raw.postProcessOpenAiApiKey
                : raw.postProcessApiKey) ?? "").Trim(),
            PostProcessPrompt = (raw.postProcessPrompt ?? "").Trim(),
            PostProcessTimeoutMs = Math.Clamp(raw.postProcessTimeoutMs ?? 8000, 1000, 60000),
            IdleUnloadMinutes = Math.Clamp(raw.idleUnloadMinutes ?? 5, 0, 240),
            Gpu = NormalizeGpu(raw.gpu),
            FocusBorder = raw.focusBorder ?? true,
            FocusBorderColor = ValueOrDefault(raw.focusBorderColor, "#E81123"),
            FocusBorderColorBusy = ValueOrDefault(raw.focusBorderColorBusy, "#FFB900"),
            FocusBorderColorPinned = ValueOrDefault(raw.focusBorderColorPinned, "#0078D4"),
            FocusBorderThickness = Math.Clamp(raw.focusBorderThickness ?? 4, 1, 40),
            FocusBorderOpacity = Math.Clamp(raw.focusBorderOpacity ?? 0.9f, 0.1f, 1f),
            PasteMethod = EqualsValue(raw.pasteMethod, "clipboard") ? "clipboard" : "unicode",
        };
    }

    private static HotkeyGesture? ResolveHotkey(
        string text,
        Func<string, HotkeyGesture?>? compatibilityParser)
    {
        if (HotkeyParser.TryParse(text, out var gesture)) return gesture;
        return compatibilityParser?.Invoke(text);
    }

    private static Dictionary<string, float> BuildMicSensitivity(Dictionary<string, float>? raw)
    {
        var result = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        if (raw == null) return result;

        foreach (var (name, value) in raw)
        {
            var key = (name ?? "").Trim();
            if (key.Length == 0 || value <= 0f) continue;
            result[key] = Math.Clamp(value, 0.001f, 0.5f);
        }
        return result;
    }

    private static string NormalizeMode(string? value)
    {
        var mode = (value ?? "").Trim().ToLowerInvariant();
        return mode is "toggle" or "hold" or "live" or "push" ? mode : "live";
    }

    private static string NormalizeGpu(string? value)
    {
        var gpu = (value ?? "").Trim().ToLowerInvariant();
        return gpu switch
        {
            "gpu" or "vulkan" => "gpu",
            "cpu" => "cpu",
            _ => "auto",
        };
    }

    public static string NormalizePostProcessProvider(string? value)
    {
        var provider = (value ?? "").Trim().ToLowerInvariant();
        if (provider.Length == 0 || provider == "anthropic") return "anthropic";
        return provider == "openai" ? "openai-compatible" : provider;
    }

    private static string ResolveConfiguredPath(string? value, AppPaths? paths)
        => paths?.ResolvePath(value) ?? Environment.ExpandEnvironmentVariables(value ?? "");

    private static bool EqualsValue(string? value, string expected)
        => (value ?? "").Trim().Equals(expected, StringComparison.OrdinalIgnoreCase);

    private static string ValueOrDefault(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
