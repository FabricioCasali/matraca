using System.Text.Json;

namespace Matraca;

/// <summary>Configuracao carregada de appsettings.json (copiado pro output).</summary>
internal sealed class Config
{
    public string ModelPath { get; init; } = "";
    public string Language { get; init; } = "pt";

    /// <summary>"discover" => modo descoberta de tecla. Senao, vkey resolvida.</summary>
    public bool DiscoverMode { get; init; }
    public int HotkeyVk { get; init; }
    public string HotkeyName { get; init; } = "";

    public string Mode { get; init; } = "toggle"; // "toggle" | "hold" | "live" | "push"
    public bool AutoEnter { get; init; }
    public bool Beep { get; init; } = true;
    public float BeepVolume { get; init; } = 0.8f; // 0..1 (ganho do tom/som de inicio/fim)
    public string StartSound { get; init; } = ""; // .wav opcional p/ inicio (senao usa tom)
    public string StopSound { get; init; } = "";  // .wav opcional p/ fim (senao usa tom)

    // modo "live" (VAD por pausa)
    public int SilenceMs { get; init; } = 700;        // pausa que finaliza uma frase
    public float VadThreshold { get; init; } = 0.012f; // energia (RMS) p/ considerar fala

    public int IdleUnloadMinutes { get; init; } = 5;  // descarrega modelo (libera VRAM) após ocioso; 0 = nunca
    public string Gpu { get; init; } = "auto";        // "auto" | "vulkan" | "cpu"

    // moldura na janela em foco durante a gravacao (mostra onde o texto sera colado)
    public bool FocusBorder { get; init; } = true;
    public string FocusBorderColor { get; init; } = "#E81123";   // hex html (vermelho = gravando)
    public int FocusBorderThickness { get; init; } = 4;          // px
    public float FocusBorderOpacity { get; init; } = 0.9f;       // 0..1

    internal sealed class RawConfig
    {
        public string? modelPath { get; set; }
        public string? language { get; set; }
        public string? hotkey { get; set; }
        public string? mode { get; set; }
        public bool? autoEnter { get; set; }
        public bool? beep { get; set; }
        public float? beepVolume { get; set; }
        public string? startSound { get; set; }
        public string? stopSound { get; set; }
        public int? silenceMs { get; set; }
        public float? vadThreshold { get; set; }
        public int? idleUnloadMinutes { get; set; }
        public string? gpu { get; set; }
        public bool? focusBorder { get; set; }
        public string? focusBorderColor { get; set; }
        public int? focusBorderThickness { get; set; }
        public float? focusBorderOpacity { get; set; }
    }

    /// <summary>Caminho do appsettings.json do usuario (gravavel sem admin).</summary>
    public static string UserConfigPath => Path.Combine(Logger.DataDir, "appsettings.json");

    /// <summary>Le a config bruta (arquivo do usuario, senao o da pasta do exe).</summary>
    internal static RawConfig LoadRaw()
    {
        var path = File.Exists(UserConfigPath)
            ? UserConfigPath
            : Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (!File.Exists(path)) return new RawConfig();
        try
        {
            return JsonSerializer.Deserialize<RawConfig>(File.ReadAllText(path),
                       new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                   ?? new RawConfig();
        }
        catch (Exception ex)
        {
            Logger.Error("Falha ao ler appsettings.json", ex);
            return new RawConfig();
        }
    }

    /// <summary>Grava a config no arquivo do usuario (%LOCALAPPDATA%\Matraca).</summary>
    internal static void SaveRaw(RawConfig raw)
    {
        Directory.CreateDirectory(Logger.DataDir);
        var json = JsonSerializer.Serialize(raw, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        });
        File.WriteAllText(UserConfigPath, json);
        Logger.Info($"Config salva em {UserConfigPath}");
    }

    public static Config Load()
    {
        // 1) %LOCALAPPDATA%\Matraca\appsettings.json (editavel sem admin, sob uiAccess);
        // 2) fallback: ao lado do exe (dev build / primeira execucao).
        MigrateLegacyConfig(UserConfigPath);
        var raw = LoadRaw();

        var modelPath = Environment.ExpandEnvironmentVariables(raw.modelPath ?? "");
        var hotkey = (raw.hotkey ?? "discover").Trim();
        var discover = hotkey.Equals("discover", StringComparison.OrdinalIgnoreCase);
        var (vk, name) = discover ? (0, "discover") : ResolveKey(hotkey);

        return new Config
        {
            ModelPath = modelPath,
            Language = string.IsNullOrWhiteSpace(raw.language) ? "pt" : raw.language!,
            DiscoverMode = discover,
            HotkeyVk = vk,
            HotkeyName = name,
            Mode = (raw.mode ?? "toggle").Trim().ToLowerInvariant(),
            AutoEnter = raw.autoEnter ?? false,
            Beep = raw.beep ?? true,
            BeepVolume = Math.Clamp(raw.beepVolume ?? 0.8f, 0f, 1f),
            StartSound = Environment.ExpandEnvironmentVariables(raw.startSound ?? ""),
            StopSound = Environment.ExpandEnvironmentVariables(raw.stopSound ?? ""),
            SilenceMs = raw.silenceMs ?? 700,
            VadThreshold = raw.vadThreshold ?? 0.012f,
            IdleUnloadMinutes = raw.idleUnloadMinutes ?? 5,
            Gpu = string.IsNullOrWhiteSpace(raw.gpu) ? "auto" : raw.gpu!.Trim().ToLowerInvariant(),
            FocusBorder = raw.focusBorder ?? true,
            FocusBorderColor = string.IsNullOrWhiteSpace(raw.focusBorderColor) ? "#E81123" : raw.focusBorderColor!.Trim(),
            FocusBorderThickness = Math.Clamp(raw.focusBorderThickness ?? 4, 1, 40),
            FocusBorderOpacity = Math.Clamp(raw.focusBorderOpacity ?? 0.9f, 0.1f, 1f),
        };
    }

    /// <summary>Traz o appsettings.json da instalacao antiga (Ditador) na primeira execucao.</summary>
    private static void MigrateLegacyConfig(string userPath)
    {
        try
        {
            if (File.Exists(userPath)) return;
            var legacy = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Ditador", "appsettings.json");
            if (!File.Exists(legacy)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(userPath)!);
            File.Copy(legacy, userPath);
            Logger.Info($"Config migrada da instalacao antiga (Ditador): {legacy} -> {userPath}");
        }
        catch (Exception ex) { Logger.Warn("Falha ao migrar config antiga: " + ex.Message); }
    }

    /// <summary>Aceita "F13".."F24", nomes de media keys, ou numero (decimal/0xHEX).</summary>
    public static (int vk, string name) ResolveKey(string s)
    {
        s = s.Trim();
        if (KeyNames.TryGetValue(s, out var vk))
            return (vk, s.ToUpperInvariant());

        // numerico: 0x.. (hex) ou decimal
        try
        {
            int n = s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? Convert.ToInt32(s, 16)
                : int.Parse(s);
            return (n, NameForVk(n));
        }
        catch
        {
            Logger.Warn($"Tecla '{s}' nao reconhecida; caindo em modo descoberta.");
            return (0, "discover");
        }
    }

    public static string NameForVk(int vk)
    {
        foreach (var kv in KeyNames)
            if (kv.Value == vk) return kv.Key;
        return $"VK_0x{vk:X2}";
    }

    // Teclas uteis p/ atalho dedicado: F-keys (incl. F13-F24), media keys, app keys.
    private static readonly Dictionary<string, int> KeyNames =
        new(StringComparer.OrdinalIgnoreCase)
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
    };
}
