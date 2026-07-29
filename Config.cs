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
    public KeyMods HotkeyMods { get; init; }
    public string HotkeyName { get; init; } = "";

    /// <summary>Tecla que fixa/solta a janela de destino do ditado. 0 = recurso desligado.</summary>
    public int PinHotkeyVk { get; init; }
    public KeyMods PinHotkeyMods { get; init; }
    public string PinHotkeyName { get; init; } = "";

    /// <summary>
    /// Como entregar na janela fixada.
    /// "focus": traz a janela pra frente, digita e devolve o foco — funciona em qualquer app,
    ///          ao custo de a janela piscar. É o padrão porque é o que realmente funciona.
    /// "nofocus": posta a mensagem sem trazer a janela pra frente. Mais elegante, mas só
    ///          funciona em campos Win32 clássicos — terminal e Electron ignoram.
    /// </summary>
    public string PinDelivery { get; init; } = "focus";

    public string Mode { get; init; } = "toggle"; // "toggle" | "hold" | "live" | "push"
    public bool AutoEnter { get; init; }
    public bool Beep { get; init; } = true;
    public float BeepVolume { get; init; } = 0.8f; // 0..1 (ganho do tom/som de inicio/fim)
    public string StartSound { get; init; } = ""; // .wav opcional p/ inicio (senao usa tom)
    public string StopSound { get; init; } = "";  // .wav opcional p/ fim (senao usa tom)

    // modo "live" (VAD por pausa)
    public int SilenceMs { get; init; } = 450;        // pausa que finaliza uma frase
    public float VadThreshold { get; init; } = 0.012f; // energia (RMS) p/ considerar fala

    /// <summary>
    /// Segundos de fala contínua a partir dos quais uma pausa curta já encerra a frase.
    /// Menor = texto sai em pedaços menores e mais rápido; maior = frases mais inteiras.
    /// </summary>
    public int PhraseMaxSeconds { get; init; } = 6;

    /// <summary>
    /// Nome do microfone a usar. Vazio = dispositivo padrão do Windows. Guardamos o nome e não
    /// o índice porque o índice muda quando se pluga/despluga um dispositivo.
    /// </summary>
    public string InputDevice { get; init; } = "";

    /// <summary>
    /// Termos que o Whisper costuma errar (nomes próprios, jargão, siglas). Vão como prompt
    /// inicial do modelo, que passa a considerá-los ao decidir o que ouviu.
    /// </summary>
    public string[] Vocabulary { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Guarda as transcrições recentes em disco (%LOCALAPPDATA%\Matraca\history.json).
    /// Desligue se não quiser o que você dita gravado em texto puro.
    /// </summary>
    public bool History { get; init; } = true;
    public int HistoryMaxItems { get; init; } = 100;

    // pós-processamento opcional do texto por um modelo Claude (pontuação, muletas)
    public bool PostProcess { get; init; }
    public string PostProcessModel { get; init; } = "claude-opus-5";
    public string PostProcessApiKey { get; init; } = "";   // vazio = usa ANTHROPIC_API_KEY
    public string PostProcessPrompt { get; init; } = "";   // vazio = instrução padrão embutida
    public int PostProcessTimeoutMs { get; init; } = 8000;

    public int IdleUnloadMinutes { get; init; } = 5;  // descarrega modelo (libera VRAM) após ocioso; 0 = nunca
    public string Gpu { get; init; } = "auto";        // "auto" | "vulkan" | "cpu"

    // moldura na janela em foco durante a gravacao (mostra onde o texto sera colado)
    public bool FocusBorder { get; init; } = true;
    public string FocusBorderColor { get; init; } = "#E81123";       // vermelho = gravando
    public string FocusBorderColorBusy { get; init; } = "#FFB900";   // âmbar = transcrevendo
    public string FocusBorderColorPinned { get; init; } = "#0078D4"; // azul = destino fixo
    public int FocusBorderThickness { get; init; } = 4;          // px
    public float FocusBorderOpacity { get; init; } = 0.9f;       // 0..1
    public string PasteMethod { get; init; } = "unicode";        // "unicode" | "clipboard"

    internal sealed class RawConfig
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
        public string? inputDevice { get; set; }
        public string[]? vocabulary { get; set; }
        public bool? history { get; set; }
        public int? historyMaxItems { get; set; }
        public bool? postProcess { get; set; }
        public string? postProcessModel { get; set; }
        public string? postProcessApiKey { get; set; }
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
        var (vk, mods, name) = discover ? (0, KeyMods.None, "discover") : ParseHotkey(hotkey);
        if (!discover && vk == 0)
        {
            Logger.Warn($"Atalho '{hotkey}' invalido; caindo em modo descoberta.");
            discover = true; name = "discover";
        }
        var (pinVk, pinMods, pinName) = ResolvePinKey(raw.pinHotkey, vk, mods);

        return new Config
        {
            ModelPath = modelPath,
            Language = string.IsNullOrWhiteSpace(raw.language) ? "pt" : raw.language!,
            DiscoverMode = discover,
            HotkeyVk = vk,
            HotkeyMods = mods,
            HotkeyName = name,
            PinHotkeyVk = pinVk,
            PinHotkeyMods = pinMods,
            PinHotkeyName = pinName,
            // qualquer coisa fora de "nofocus" cai no modo que funciona em todo lugar
            PinDelivery = (raw.pinDelivery ?? "").Trim().Equals("nofocus", StringComparison.OrdinalIgnoreCase)
                ? "nofocus" : "focus",
            Mode = (raw.mode ?? "toggle").Trim().ToLowerInvariant(),
            AutoEnter = raw.autoEnter ?? false,
            Beep = raw.beep ?? true,
            BeepVolume = Math.Clamp(raw.beepVolume ?? 0.8f, 0f, 1f),
            StartSound = Environment.ExpandEnvironmentVariables(raw.startSound ?? ""),
            StopSound = Environment.ExpandEnvironmentVariables(raw.stopSound ?? ""),
            SilenceMs = raw.silenceMs ?? 450,
            PhraseMaxSeconds = Math.Clamp(raw.phraseMaxSeconds ?? 6, 2, 20),
            VadThreshold = raw.vadThreshold ?? 0.012f,
            InputDevice = (raw.inputDevice ?? "").Trim(),
            Vocabulary = (raw.vocabulary ?? Array.Empty<string>())
                .Select(v => (v ?? "").Trim())
                .Where(v => v.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            History = raw.history ?? true,
            HistoryMaxItems = Math.Clamp(raw.historyMaxItems ?? 100, 1, 5000),
            PostProcess = raw.postProcess ?? false,
            PostProcessModel = string.IsNullOrWhiteSpace(raw.postProcessModel)
                ? "claude-opus-5" : raw.postProcessModel!.Trim(),
            PostProcessApiKey = (raw.postProcessApiKey ?? "").Trim(),
            PostProcessPrompt = (raw.postProcessPrompt ?? "").Trim(),
            PostProcessTimeoutMs = Math.Clamp(raw.postProcessTimeoutMs ?? 8000, 1000, 60000),
            IdleUnloadMinutes = raw.idleUnloadMinutes ?? 5,
            Gpu = string.IsNullOrWhiteSpace(raw.gpu) ? "auto" : raw.gpu!.Trim().ToLowerInvariant(),
            FocusBorder = raw.focusBorder ?? true,
            FocusBorderColor = string.IsNullOrWhiteSpace(raw.focusBorderColor) ? "#E81123" : raw.focusBorderColor!.Trim(),
            FocusBorderColorBusy = string.IsNullOrWhiteSpace(raw.focusBorderColorBusy) ? "#FFB900" : raw.focusBorderColorBusy!.Trim(),
            FocusBorderColorPinned = string.IsNullOrWhiteSpace(raw.focusBorderColorPinned) ? "#0078D4" : raw.focusBorderColorPinned!.Trim(),
            FocusBorderThickness = Math.Clamp(raw.focusBorderThickness ?? 4, 1, 40),
            FocusBorderOpacity = Math.Clamp(raw.focusBorderOpacity ?? 0.9f, 0.1f, 1f),
            // qualquer coisa fora de "clipboard" cai no padrao — um valor invalido no JSON
            // deixaria o combo da tela de config sem selecao e travaria o Salvar.
            PasteMethod = (raw.pasteMethod ?? "").Trim().Equals("clipboard", StringComparison.OrdinalIgnoreCase)
                ? "clipboard" : "unicode",
        };
    }

    /// <summary>
    /// Resolve a tecla de fixar janela. Vazio/"none" desliga o recurso, e o atalho nao pode
    /// colidir com o do ditado (o hook engole a tecla alvo, entao um anularia o outro).
    /// </summary>
    private static (int vk, KeyMods mods, string name) ResolvePinKey(
        string? raw, int dictationVk, KeyMods dictationMods)
    {
        var s = (raw ?? "").Trim();
        if (s.Length == 0 || s.Equals("none", StringComparison.OrdinalIgnoreCase))
            return (0, KeyMods.None, "");

        var (vk, mods, name) = ParseHotkey(s);
        if (vk == 0)
        {
            Logger.Warn($"pinHotkey '{s}' nao reconhecida; fixar janela ficou desligado.");
            return (0, KeyMods.None, "");
        }
        if (vk == dictationVk && mods == dictationMods)
        {
            Logger.Warn($"pinHotkey '{s}' e' o mesmo atalho do ditado; fixar janela ficou desligado.");
            return (0, KeyMods.None, "");
        }
        return (vk, mods, name);
    }

    /// <summary>
    /// Aceita tecla unica ("F15", "MediaPlayPause", "0xB6") ou combo ("Ctrl+Alt+X").
    /// Devolve vk=0 quando nao da pra usar o que foi pedido.
    /// </summary>
    public static (int vk, KeyMods mods, string name) ParseHotkey(string s)
    {
        s = (s ?? "").Trim();
        if (s.Length == 0) return (0, KeyMods.None, "");

        var parts = s.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var mods = KeyMods.None;
        string? baseKey = null;
        foreach (var p in parts)
        {
            switch (p.ToLowerInvariant())
            {
                case "ctrl": case "control": mods |= KeyMods.Ctrl; break;
                case "alt": mods |= KeyMods.Alt; break;
                case "shift": mods |= KeyMods.Shift; break;
                case "win": case "windows": mods |= KeyMods.Win; break;
                default:
                    if (baseKey != null)
                    {
                        Logger.Warn($"Atalho '{s}' tem mais de uma tecla base.");
                        return (0, KeyMods.None, "");
                    }
                    baseKey = p;
                    break;
            }
        }
        if (baseKey == null)
        {
            Logger.Warn($"Atalho '{s}' so tem modificadores, falta a tecla.");
            return (0, KeyMods.None, "");
        }

        var (vk, _) = ResolveKey(baseKey);
        if (vk == 0) return (0, KeyMods.None, "");

        // Letra/digito solto sequestraria a tecla no sistema inteiro (o hook engole a tecla
        // alvo), deixando o usuario sem conseguir digitar. So vale acompanhado de modificador.
        if (mods == KeyMods.None && IsTypingKey(vk))
        {
            Logger.Warn($"Atalho '{s}': tecla de digitacao sem modificador nao e' aceita " +
                        "(ela pararia de funcionar no sistema inteiro). Use algo como Ctrl+Alt+" + baseKey + ".");
            return (0, KeyMods.None, "");
        }
        return (vk, mods, FormatHotkey(vk, mods));
    }

    /// <summary>Letras e digitos: teclas que o usuario precisa pra escrever.</summary>
    private static bool IsTypingKey(int vk) => vk is (>= 0x30 and <= 0x39) or (>= 0x41 and <= 0x5A);

    /// <summary>
    /// Formata o atalho pro JSON e pra tela ("Ctrl+Alt+X"). O resultado sempre volta a ser
    /// lido por <see cref="ParseHotkey"/> — tecla sem nome conhecido vira hex ("0x7E"), e nao
    /// o rotulo "VK_0x7E", que nao seria reconhecido na volta.
    /// </summary>
    public static string FormatHotkey(int vk, KeyMods mods)
    {
        var parts = new List<string>(5);
        if (mods.HasFlag(KeyMods.Ctrl)) parts.Add("Ctrl");
        if (mods.HasFlag(KeyMods.Alt)) parts.Add("Alt");
        if (mods.HasFlag(KeyMods.Shift)) parts.Add("Shift");
        if (mods.HasFlag(KeyMods.Win)) parts.Add("Win");

        var name = NameForVk(vk);
        parts.Add(name.StartsWith("VK_0x", StringComparison.Ordinal) ? $"0x{vk:X2}" : name);
        return string.Join("+", parts);
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
    // Letras e digitos entram depois (BuildKeyNames), so utilizaveis em combo.
    private static readonly Dictionary<string, int> KeyNames = BuildKeyNames();

    private static Dictionary<string, int> BuildKeyNames()
    {
        var d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
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

        // adicionadas por ultimo p/ nao virarem o nome preferido em NameForVk
        for (int vk = 0x41; vk <= 0x5A; vk++) d[((char)vk).ToString()] = vk;   // A-Z
        for (int vk = 0x30; vk <= 0x39; vk++) d[((char)vk).ToString()] = vk;   // 0-9
        return d;
    }
}
