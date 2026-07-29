using System.Windows.Forms;

namespace Matraca;

/// <summary>
/// Tela de configuracoes: edita o appsettings.json do usuario (%LOCALAPPDATA%\Matraca).
/// Organizada em abas porque a lista de opcoes ficou grande demais p/ uma coluna so'.
///
/// A captura de atalho usa um hook de descoberta proprio e SUSPENDE o hook principal
/// enquanto captura (senao apertar o atalho atual dispararia uma gravacao).
/// Retorna DialogResult.OK quando salvou; quem chama decide reiniciar o app.
/// </summary>
internal sealed class SettingsForm : Form
{
    private readonly HotkeyListener? _mainHotkey;   // p/ suspender durante a captura
    private HotkeyListener? _capture;               // hook de descoberta temporario

    // -- aba Ditado --
    private readonly TextBox _hotkeyBox;
    private readonly Button _captureBtn;
    private readonly TextBox _pinHotkeyBox;
    private readonly Button _pinCaptureBtn;
    private readonly ComboBox _pinDeliveryBox;
    private readonly ComboBox _modeBox;
    private readonly ComboBox _languageBox;
    private readonly CheckBox _autoEnterBox;
    private readonly ComboBox _pasteMethodBox;

    // -- aba Áudio --
    private readonly ComboBox _inputDeviceBox;
    private readonly CheckBox _beepBox;
    private readonly NumericUpDown _beepVolumeBox;
    private readonly TextBox _startSoundBox;
    private readonly TextBox _stopSoundBox;
    private readonly NumericUpDown _silenceMsBox;
    private readonly NumericUpDown _phraseMaxBox;
    private readonly NumericUpDown _vadThresholdBox;

    // -- aba Visual --
    private readonly CheckBox _borderBox;
    private readonly Button _borderColorBtn;
    private readonly Button _borderColorBusyBtn;
    private readonly Button _borderColorPinnedBtn;
    private readonly NumericUpDown _borderThicknessBox;
    private readonly NumericUpDown _borderOpacityBox;

    // -- aba Modelo --
    private readonly TextBox _modelPathBox;
    private readonly ComboBox _gpuBox;
    private readonly NumericUpDown _idleUnloadBox;
    private readonly TextBox _vocabularyBox;

    // -- aba Avançado --
    private readonly CheckBox _historyBox;
    private readonly NumericUpDown _historyMaxBox;
    private readonly CheckBox _postProcessBox;
    private readonly TextBox _postModelBox;
    private readonly TextBox _postApiKeyBox;
    private readonly NumericUpDown _postTimeoutBox;
    private readonly TextBox _postPromptBox;

    private string _hotkeyValue;      // o que vai pro JSON ("F15", "Ctrl+Alt+X" ou "discover")
    private string _pinHotkeyValue;   // idem p/ fixar janela; vazio = recurso desligado
    private bool _capturingPin;       // qual dos dois campos esta capturando agora
    private Color _borderColor, _borderColorBusy, _borderColorPinned;

    private const string PinOffLabel = "(desligado)";
    private const string CapturingLabel = "pressione uma tecla...";

    private static readonly (string Value, string Label)[] Modes =
    {
        ("toggle", "toggle — aperta liga, aperta desliga"),
        ("hold",   "hold — segura a tecla enquanto fala"),
        ("live",   "live — sessão contínua, cola a cada pausa"),
        ("push",   "push — segura a tecla, cola a cada pausa"),
    };

    public SettingsForm(HotkeyListener? mainHotkey)
    {
        _mainHotkey = mainHotkey;
        var cfg = Config.Load();
        var raw = Config.LoadRaw();

        _hotkeyValue = cfg.DiscoverMode ? "discover" : Config.FormatHotkey(cfg.HotkeyVk, cfg.HotkeyMods);
        _pinHotkeyValue = cfg.PinHotkeyVk == 0
            ? "" : Config.FormatHotkey(cfg.PinHotkeyVk, cfg.PinHotkeyMods);
        _borderColor = ParseColorSafe(cfg.FocusBorderColor, 0xE81123);
        _borderColorBusy = ParseColorSafe(cfg.FocusBorderColorBusy, 0xFFB900);
        _borderColorPinned = ParseColorSafe(cfg.FocusBorderColorPinned, 0x0078D4);

        Text = "Matraca — Configurações";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9f);
        ClientSize = new Size(620, 560);

        var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(12, 6) };

        // ============================ DITADO ============================
        var gDictation = NewGrid();

        var hotkeyPanel = NewRowPanel();
        _hotkeyBox = new TextBox { ReadOnly = true, Width = 150, Text = cfg.HotkeyName };
        _captureBtn = new Button { Text = "Capturar...", AutoSize = true };
        _captureBtn.Click += (_, _) => ToggleCapture(pin: false);
        hotkeyPanel.Controls.Add(_hotkeyBox);
        hotkeyPanel.Controls.Add(_captureBtn);
        AddRow(gDictation, "Tecla de atalho", hotkeyPanel,
            "Clique em Capturar e pressione a tecla (Esc cancela). Aceita combos como Ctrl+Alt+X; "
          + "teclas de digitação sozinhas não são aceitas.");

        var pinPanel = NewRowPanel();
        _pinHotkeyBox = new TextBox
        {
            ReadOnly = true,
            Width = 150,
            Text = cfg.PinHotkeyVk == 0 ? PinOffLabel : cfg.PinHotkeyName,
        };
        _pinCaptureBtn = new Button { Text = "Capturar...", AutoSize = true };
        _pinCaptureBtn.Click += (_, _) => ToggleCapture(pin: true);
        var pinClearBtn = new Button { Text = "Limpar", AutoSize = true };
        pinClearBtn.Click += (_, _) => { _pinHotkeyValue = ""; _pinHotkeyBox.Text = PinOffLabel; };
        pinPanel.Controls.Add(_pinHotkeyBox);
        pinPanel.Controls.Add(_pinCaptureBtn);
        pinPanel.Controls.Add(pinClearBtn);
        AddRow(gDictation, "Fixar janela de destino", pinPanel,
            "Aperta e o ditado passa a ir sempre pra janela que estava em foco, mesmo que você "
          + "mude de janela depois.");

        _pinDeliveryBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320 };
        _pinDeliveryBox.Items.AddRange(new object[]
        {
            "focus — traz a janela pra frente e devolve o foco (funciona sempre)",
            "nofocus — entrega em silêncio (só campos Win32 clássicos)",
        });
        _pinDeliveryBox.SelectedIndex = cfg.PinDelivery == "nofocus" ? 1 : 0;
        AddRow(gDictation, "Entrega no destino fixo", _pinDeliveryBox,
            "O modo silencioso não traz a janela pra frente, mas terminal, console e apps "
          + "Electron ignoram — nesses o texto simplesmente não aparece.");

        _modeBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320 };
        foreach (var (_, label) in Modes) _modeBox.Items.Add(label);
        _modeBox.SelectedIndex = Math.Max(0, Array.FindIndex(Modes, m => m.Value == cfg.Mode));
        AddRow(gDictation, "Modo de ditado", _modeBox);

        _languageBox = new ComboBox { Width = 120, Text = cfg.Language };
        _languageBox.Items.AddRange(new object[] { "pt", "en", "es", "auto" });
        AddRow(gDictation, "Idioma", _languageBox);

        _autoEnterBox = new CheckBox { Text = "Pressionar Enter após colar", Checked = cfg.AutoEnter, AutoSize = true };
        AddRow(gDictation, "Auto-Enter", _autoEnterBox);

        _pasteMethodBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
        _pasteMethodBox.Items.AddRange(new object[] { "unicode", "clipboard" });
        _pasteMethodBox.SelectedItem = cfg.PasteMethod;
        AddRow(gDictation, "Método de colagem", _pasteMethodBox,
            "unicode: digita direto, não encosta no clipboard. clipboard: Ctrl+V tradicional.");

        tabs.TabPages.Add(NewTab("Ditado", gDictation));

        // ============================ ÁUDIO =============================
        var gAudio = NewGrid();

        _inputDeviceBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320 };
        _inputDeviceBox.Items.Add("(padrão do Windows)");
        foreach (var name in AudioDevices.ListNames()) _inputDeviceBox.Items.Add(name);
        _inputDeviceBox.SelectedIndex = 0;
        if (cfg.InputDevice.Length > 0)
        {
            int idx = _inputDeviceBox.Items.IndexOf(cfg.InputDevice);
            // dispositivo salvo mas desconectado: mostra assim mesmo p/ nao perder a escolha
            if (idx < 0) idx = _inputDeviceBox.Items.Add(cfg.InputDevice + "  (desconectado)");
            _inputDeviceBox.SelectedIndex = idx;
        }
        AddRow(gAudio, "Microfone", _inputDeviceBox);

        var beepPanel = NewRowPanel();
        _beepBox = new CheckBox { Text = "Sons de início/fim", Checked = cfg.Beep, AutoSize = true };
        _beepVolumeBox = NewNumeric(0m, 1m, (decimal)cfg.BeepVolume, 0.1m, 1);
        beepPanel.Controls.Add(_beepBox);
        beepPanel.Controls.Add(new Label { Text = "volume:", AutoSize = true, Padding = new Padding(8, 5, 0, 0) });
        beepPanel.Controls.Add(_beepVolumeBox);
        AddRow(gAudio, "Feedback sonoro", beepPanel);

        _startSoundBox = new TextBox { Width = 220, Text = raw.startSound ?? "" };
        AddRow(gAudio, "Som de início", SoundPanel(_startSoundBox),
            "Arquivo .wav/.mp3 opcional. Vazio = tom sintético (subindo).");

        _stopSoundBox = new TextBox { Width = 220, Text = raw.stopSound ?? "" };
        AddRow(gAudio, "Som de fim", SoundPanel(_stopSoundBox),
            "Vazio = tom sintético (descendo).");

        _silenceMsBox = NewNumeric(200m, 5000m, cfg.SilenceMs, 50m, 0);
        AddRow(gAudio, "Pausa p/ frase (ms)", _silenceMsBox,
            "Modos live/push: silêncio que encerra uma frase. Menor = texto sai mais rápido.");

        _phraseMaxBox = NewNumeric(2m, 20m, cfg.PhraseMaxSeconds, 1m, 0);
        AddRow(gAudio, "Corte suave após (s)", _phraseMaxBox,
            "Passando disto numa fala contínua, uma pausa curta já encerra a frase — evita "
          + "esperar o limite de 20s e colar tudo de uma vez.");

        _vadThresholdBox = NewNumeric(0.001m, 0.2m, (decimal)cfg.VadThreshold, 0.001m, 3);
        AddRow(gAudio, "Sensibilidade VAD", _vadThresholdBox,
            "Energia mínima p/ considerar fala. Maior = ignora mais ruído.");

        tabs.TabPages.Add(NewTab("Áudio", gAudio));

        // ============================ VISUAL ============================
        var gVisual = NewGrid();

        _borderBox = new CheckBox { Text = "Marcar janela em foco durante a gravação", Checked = cfg.FocusBorder, AutoSize = true };
        AddRow(gVisual, "Moldura de foco", _borderBox,
            "Mostra em qual janela o texto será entregue.");

        var colorPanel = NewRowPanel();
        _borderColorBtn = ColorButton("Gravando", _borderColor, c => _borderColor = c);
        _borderColorBusyBtn = ColorButton("Transcrevendo", _borderColorBusy, c => _borderColorBusy = c);
        _borderColorPinnedBtn = ColorButton("Fixado", _borderColorPinned, c => _borderColorPinned = c);
        colorPanel.Controls.Add(_borderColorBtn);
        colorPanel.Controls.Add(_borderColorBusyBtn);
        colorPanel.Controls.Add(_borderColorPinnedBtn);
        AddRow(gVisual, "Cores por estado", colorPanel,
            "A moldura troca de cor conforme o estado, sem mudar de janela.");

        _borderThicknessBox = NewNumeric(1m, 40m, cfg.FocusBorderThickness, 1m, 0);
        AddRow(gVisual, "Espessura (px)", _borderThicknessBox);

        _borderOpacityBox = NewNumeric(0.1m, 1m, (decimal)cfg.FocusBorderOpacity, 0.1m, 1);
        AddRow(gVisual, "Opacidade", _borderOpacityBox);

        tabs.TabPages.Add(NewTab("Visual", gVisual));

        // ============================ MODELO ============================
        var gModel = NewGrid();

        var modelPanel = NewRowPanel();
        _modelPathBox = new TextBox { Width = 270, Text = raw.modelPath ?? cfg.ModelPath };
        var browseBtn = new Button { Text = "...", Width = 32 };
        browseBtn.Click += (_, _) => BrowseModel();
        modelPanel.Controls.Add(_modelPathBox);
        modelPanel.Controls.Add(browseBtn);
        AddRow(gModel, "Modelo Whisper (.bin)", modelPanel);

        _gpuBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
        _gpuBox.Items.AddRange(new object[] { "auto", "vulkan", "cpu" });
        _gpuBox.SelectedItem = cfg.Gpu is "vulkan" or "cpu" ? cfg.Gpu : "auto";
        AddRow(gModel, "Processamento", _gpuBox);

        _idleUnloadBox = NewNumeric(0m, 240m, cfg.IdleUnloadMinutes, 1m, 0);
        AddRow(gModel, "Liberar VRAM após (min)", _idleUnloadBox, "0 = nunca descarregar o modelo.");

        _vocabularyBox = new TextBox
        {
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Width = 320,
            Height = 110,
            Text = string.Join(Environment.NewLine, cfg.Vocabulary),
        };
        AddRow(gModel, "Vocabulário", _vocabularyBox,
            "Um termo por linha: nomes próprios, siglas e jargão que o Whisper costuma errar. "
          + "Vira o prompt inicial do modelo.");

        tabs.TabPages.Add(NewTab("Modelo", gModel));

        // =========================== AVANÇADO ===========================
        var gAdv = NewGrid();

        var histPanel = NewRowPanel();
        _historyBox = new CheckBox { Text = "Guardar transcrições recentes", Checked = cfg.History, AutoSize = true };
        _historyMaxBox = NewNumeric(1m, 5000m, cfg.HistoryMaxItems, 10m, 0);
        histPanel.Controls.Add(_historyBox);
        histPanel.Controls.Add(new Label { Text = "máx.:", AutoSize = true, Padding = new Padding(8, 5, 0, 0) });
        histPanel.Controls.Add(_historyMaxBox);
        AddRow(gAdv, "Histórico", histPanel,
            "Grava em texto puro no disco (%LOCALAPPDATA%\\Matraca\\history.json) tudo o que "
          + "você ditar. Desligue se isso não for aceitável.");

        _postProcessBox = new CheckBox
        {
            Text = "Limpar o texto com um modelo Claude",
            Checked = cfg.PostProcess,
            AutoSize = true,
        };
        AddRow(gAdv, "Pós-processamento", _postProcessBox,
            "Corrige pontuação e tira muletas de fala. Custa uma ida à rede por ditado (por "
          + "frase, nos modos live/push) e usa a API da Anthropic, que é paga.");

        _postModelBox = new TextBox { Width = 220, Text = cfg.PostProcessModel };
        AddRow(gAdv, "Modelo", _postModelBox);

        _postApiKeyBox = new TextBox { Width = 220, Text = cfg.PostProcessApiKey, UseSystemPasswordChar = true };
        AddRow(gAdv, "Chave de API", _postApiKeyBox,
            "Vazio = usa a variável de ambiente ANTHROPIC_API_KEY.");

        _postTimeoutBox = NewNumeric(1000m, 60000m, cfg.PostProcessTimeoutMs, 500m, 0);
        AddRow(gAdv, "Timeout (ms)", _postTimeoutBox,
            "Estourando o tempo, entrega a transcrição original sem limpar.");

        _postPromptBox = new TextBox
        {
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Width = 320,
            Height = 90,
            Text = cfg.PostProcessPrompt,
        };
        AddRow(gAdv, "Instrução", _postPromptBox, "Vazio = usa a instrução padrão embutida.");

        tabs.TabPages.Add(NewTab("Avançado", gAdv));

        // ---- botoes ----
        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Bottom,
            AutoSize = true,
            Padding = new Padding(0, 8, 12, 10),
        };
        var cancelBtn = new Button { Text = "Cancelar", AutoSize = true, DialogResult = DialogResult.Cancel };
        var saveBtn = new Button { Text = "Salvar", AutoSize = true };
        saveBtn.Click += (_, _) => Save();
        buttons.Controls.Add(cancelBtn);
        buttons.Controls.Add(saveBtn);
        AcceptButton = saveBtn;
        CancelButton = cancelBtn;

        Controls.Add(tabs);
        Controls.Add(buttons);
        FormClosed += (_, _) => StopCapture();
    }

    // ---- captura de tecla (serve aos dois campos: ditado e fixar janela) ----
    private void ToggleCapture(bool pin)
    {
        if (_capture != null) { StopCapture(); return; }

        _capturingPin = pin;
        if (_mainHotkey != null) _mainHotkey.Suspended = true;
        _capture = HotkeyListener.CreateDiscovery();
        _capture.KeyDiscovered += OnKeyCaptured;
        _capture.Start();
        (pin ? _pinHotkeyBox : _hotkeyBox).Text = CapturingLabel;
        (pin ? _pinCaptureBtn : _captureBtn).Text = "Cancelar";
    }

    private void OnKeyCaptured(int vk, KeyMods mods)
    {
        if (vk == 0x1B && mods == KeyMods.None) { RestoreDisplay(_capturingPin); StopCapture(); return; } // Esc cancela

        var combo = Config.FormatHotkey(vk, mods);
        // revalida: letra/dígito solto é recusado, então não deixa salvar um atalho morto
        if (Config.ParseHotkey(combo).vk == 0)
        {
            MessageBox.Show(this,
                $"'{combo}' não serve como atalho: teclas de digitação sozinhas parariam de "
              + "funcionar no sistema inteiro. Junte um modificador (ex.: Ctrl+Alt+"
              + Config.NameForVk(vk) + ") ou use uma tecla dedicada como F13–F24.",
                "Matraca", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            RestoreDisplay(_capturingPin);
            StopCapture();
            return;
        }

        if (_capturingPin) { _pinHotkeyValue = combo; _pinHotkeyBox.Text = combo; }
        else { _hotkeyValue = combo; _hotkeyBox.Text = combo; }
        StopCapture();
    }

    private void StopCapture()
    {
        if (_capture != null)
        {
            _capture.KeyDiscovered -= OnKeyCaptured;
            _capture.Dispose();
            _capture = null;
        }
        if (_mainHotkey != null) _mainHotkey.Suspended = false;
        _captureBtn.Text = "Capturar...";
        _pinCaptureBtn.Text = "Capturar...";
        if (_hotkeyBox.Text == CapturingLabel) RestoreDisplay(false);
        if (_pinHotkeyBox.Text == CapturingLabel) RestoreDisplay(true);
    }

    private void RestoreDisplay(bool pin)
    {
        // _hotkeyValue/_pinHotkeyValue ja estao no formato de exibicao ("Ctrl+Alt+X")
        if (pin) _pinHotkeyBox.Text = _pinHotkeyValue.Length == 0 ? PinOffLabel : _pinHotkeyValue;
        else _hotkeyBox.Text = _hotkeyValue;
    }

    // ---- demais campos ----
    private Button ColorButton(string label, Color initial, Action<Color> set)
    {
        var btn = new Button { Text = label, AutoSize = true, BackColor = initial, ForeColor = Contrast(initial) };
        btn.Click += (_, _) =>
        {
            using var dlg = new ColorDialog { Color = btn.BackColor, FullOpen = true };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            set(dlg.Color);
            btn.BackColor = dlg.Color;
            btn.ForeColor = Contrast(dlg.Color);
        };
        return btn;
    }

    /// <summary>Preto ou branco, o que der pra ler em cima da cor escolhida.</summary>
    private static Color Contrast(Color c)
        => (c.R * 0.299 + c.G * 0.587 + c.B * 0.114) > 150 ? Color.Black : Color.White;

    private FlowLayoutPanel SoundPanel(TextBox box)
    {
        var panel = NewRowPanel();
        var browse = new Button { Text = "...", Width = 32 };
        browse.Click += (_, _) =>
        {
            using var dlg = new OpenFileDialog
            {
                Filter = "Áudio (*.wav;*.mp3)|*.wav;*.mp3|Todos (*.*)|*.*",
                FileName = Environment.ExpandEnvironmentVariables(box.Text),
            };
            if (dlg.ShowDialog(this) == DialogResult.OK) box.Text = dlg.FileName;
        };
        var play = new Button { Text = "▶", Width = 32 };
        play.Click += (_, _) =>
        {
            var path = Environment.ExpandEnvironmentVariables(box.Text.Trim());
            if (path.Length == 0 || !File.Exists(path))
            {
                MessageBox.Show(this, "Escolha um arquivo de áudio existente para ouvir.",
                    "Matraca", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Beeper.PlaySoundFile(path, (float)_beepVolumeBox.Value);
        };
        var clear = new Button { Text = "Limpar", AutoSize = true };
        clear.Click += (_, _) => box.Text = "";
        panel.Controls.Add(box);
        panel.Controls.Add(browse);
        panel.Controls.Add(play);
        panel.Controls.Add(clear);
        return panel;
    }

    private void BrowseModel()
    {
        using var dlg = new OpenFileDialog
        {
            Filter = "Modelo Whisper ggml (*.bin)|*.bin|Todos (*.*)|*.*",
            FileName = Environment.ExpandEnvironmentVariables(_modelPathBox.Text),
        };
        if (dlg.ShowDialog(this) == DialogResult.OK) _modelPathBox.Text = dlg.FileName;
    }

    private void Save()
    {
        // o hook engole a tecla alvo, entao o mesmo atalho nos dois campos anularia o ditado
        if (_pinHotkeyValue.Length > 0 &&
            _pinHotkeyValue.Equals(_hotkeyValue, StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this,
                "A tecla de fixar janela não pode ser a mesma do ditado. Escolha outra ou limpe o campo.",
                "Matraca", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            Config.SaveRaw(new Config.RawConfig
            {
                modelPath = _modelPathBox.Text.Trim(),
                language = string.IsNullOrWhiteSpace(_languageBox.Text) ? "pt" : _languageBox.Text.Trim(),
                hotkey = _hotkeyValue,
                pinHotkey = _pinHotkeyValue.Length == 0 ? null : _pinHotkeyValue,
                pinDelivery = _pinDeliveryBox.SelectedIndex == 1 ? "nofocus" : "focus",
                mode = Modes[Math.Max(0, _modeBox.SelectedIndex)].Value,
                autoEnter = _autoEnterBox.Checked,
                pasteMethod = (string)_pasteMethodBox.SelectedItem!,
                inputDevice = SelectedInputDevice(),
                beep = _beepBox.Checked,
                beepVolume = (float)_beepVolumeBox.Value,
                startSound = NullIfBlank(_startSoundBox.Text),
                stopSound = NullIfBlank(_stopSoundBox.Text),
                silenceMs = (int)_silenceMsBox.Value,
                phraseMaxSeconds = (int)_phraseMaxBox.Value,
                vadThreshold = (float)_vadThresholdBox.Value,
                idleUnloadMinutes = (int)_idleUnloadBox.Value,
                gpu = (string)_gpuBox.SelectedItem!,
                vocabulary = ParseVocabulary(),
                focusBorder = _borderBox.Checked,
                focusBorderColor = Hex(_borderColor),
                focusBorderColorBusy = Hex(_borderColorBusy),
                focusBorderColorPinned = Hex(_borderColorPinned),
                focusBorderThickness = (int)_borderThicknessBox.Value,
                focusBorderOpacity = (float)_borderOpacityBox.Value,
                history = _historyBox.Checked,
                historyMaxItems = (int)_historyMaxBox.Value,
                postProcess = _postProcessBox.Checked,
                postProcessModel = _postModelBox.Text.Trim(),
                postProcessApiKey = _postApiKeyBox.Text.Trim(),
                postProcessPrompt = NullIfBlank(_postPromptBox.Text),
                postProcessTimeoutMs = (int)_postTimeoutBox.Value,
            });
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            Logger.Error("Falha ao salvar configuracoes", ex);
            MessageBox.Show(this, "Não consegui salvar: " + ex.Message, "Matraca",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>Vazio no JSON = padrão do Windows; o sufixo "(desconectado)" nunca é gravado.</summary>
    private string? SelectedInputDevice()
    {
        if (_inputDeviceBox.SelectedIndex <= 0) return null;
        var name = _inputDeviceBox.SelectedItem?.ToString() ?? "";
        int mark = name.IndexOf("  (desconectado)", StringComparison.Ordinal);
        return mark >= 0 ? name[..mark] : name;
    }

    private string[]? ParseVocabulary()
    {
        var terms = _vocabularyBox.Text
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return terms.Length == 0 ? null : terms;
    }

    private static string? NullIfBlank(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    // ---- helpers de layout ----
    private static TabPage NewTab(string title, Control content)
    {
        var page = new TabPage(title) { AutoScroll = true, Padding = new Padding(4) };
        page.Controls.Add(content);
        return page;
    }

    private static TableLayoutPanel NewGrid()
    {
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            Padding = new Padding(12),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 400));
        return grid;
    }

    private static FlowLayoutPanel NewRowPanel() => new()
    {
        FlowDirection = FlowDirection.LeftToRight,
        AutoSize = true,
        WrapContents = false,
        Margin = new Padding(0),
    };

    private static NumericUpDown NewNumeric(decimal min, decimal max, decimal value, decimal step, int decimals) => new()
    {
        Minimum = min,
        Maximum = max,
        Value = Math.Clamp(value, min, max),
        Increment = step,
        DecimalPlaces = decimals,
        Width = 80,
    };

    private static void AddRow(TableLayoutPanel grid, string label, Control control, string? hint = null)
    {
        grid.Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            Padding = new Padding(0, 6, 0, 0),
        });
        if (hint == null)
        {
            control.Margin = new Padding(0, 4, 0, 8);
            grid.Controls.Add(control);
            return;
        }
        var stack = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            WrapContents = false,
            Margin = new Padding(0, 4, 0, 8),
        };
        stack.Controls.Add(control);
        stack.Controls.Add(new Label
        {
            Text = hint,
            AutoSize = true,
            MaximumSize = new Size(390, 0),
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(2, 2, 0, 0),
        });
        grid.Controls.Add(stack);
    }

    private static Color ParseColorSafe(string html, int fallback)
    {
        try { return ColorTranslator.FromHtml(html); }
        catch { return Color.FromArgb(fallback >> 16 & 0xFF, fallback >> 8 & 0xFF, fallback & 0xFF); }
    }
}
