using System.Windows.Forms;

namespace Matraca;

/// <summary>
/// Tela de configuracoes: edita o appsettings.json do usuario (%LOCALAPPDATA%\Matraca).
/// A captura de atalho usa um hook de descoberta proprio e SUSPENDE o hook principal
/// enquanto captura (senao apertar o atalho atual dispararia uma gravacao).
/// Retorna DialogResult.OK quando salvou; quem chama decide reiniciar o app.
/// </summary>
internal sealed class SettingsForm : Form
{
    private readonly HotkeyListener? _mainHotkey;   // p/ suspender durante a captura
    private HotkeyListener? _capture;               // hook de descoberta temporario

    private readonly TextBox _hotkeyBox;
    private readonly Button _captureBtn;
    private readonly ComboBox _modeBox;
    private readonly ComboBox _languageBox;
    private readonly CheckBox _autoEnterBox;
    private readonly CheckBox _beepBox;
    private readonly NumericUpDown _beepVolumeBox;
    private readonly NumericUpDown _silenceMsBox;
    private readonly NumericUpDown _vadThresholdBox;
    private readonly NumericUpDown _idleUnloadBox;
    private readonly ComboBox _gpuBox;
    private readonly TextBox _modelPathBox;
    private readonly CheckBox _borderBox;
    private readonly Button _borderColorBtn;
    private readonly NumericUpDown _borderThicknessBox;
    private readonly NumericUpDown _borderOpacityBox;

    private string _hotkeyValue;   // o que vai pro JSON ("F15", "0xB6" ou "discover")
    private Color _borderColor;

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
        _hotkeyValue = cfg.DiscoverMode ? "discover" : HotkeyJsonValue(cfg.HotkeyVk);
        _borderColor = ParseColorSafe(cfg.FocusBorderColor);

        Text = "Matraca — Configurações";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9f);

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(12),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 330));

        // -- atalho --
        var hotkeyPanel = NewRowPanel();
        _hotkeyBox = new TextBox { ReadOnly = true, Width = 200, Text = cfg.HotkeyName };
        _captureBtn = new Button { Text = "Capturar...", AutoSize = true };
        _captureBtn.Click += (_, _) => ToggleCapture();
        hotkeyPanel.Controls.Add(_hotkeyBox);
        hotkeyPanel.Controls.Add(_captureBtn);
        AddRow(grid, "Tecla de atalho", hotkeyPanel,
            "Clique em Capturar e pressione a tecla desejada (Esc cancela).");

        // -- modo --
        _modeBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320 };
        foreach (var (_, label) in Modes) _modeBox.Items.Add(label);
        _modeBox.SelectedIndex = Math.Max(0, Array.FindIndex(Modes, m => m.Value == cfg.Mode));
        AddRow(grid, "Modo de ditado", _modeBox);

        // -- idioma --
        _languageBox = new ComboBox { Width = 120, Text = cfg.Language };
        _languageBox.Items.AddRange(new object[] { "pt", "en", "es", "auto" });
        AddRow(grid, "Idioma", _languageBox);

        // -- auto enter / beep --
        _autoEnterBox = new CheckBox { Text = "Pressionar Enter após colar", Checked = cfg.AutoEnter, AutoSize = true };
        AddRow(grid, "Auto-Enter", _autoEnterBox);

        var beepPanel = NewRowPanel();
        _beepBox = new CheckBox { Text = "Sons de início/fim", Checked = cfg.Beep, AutoSize = true };
        _beepVolumeBox = NewNumeric(0m, 1m, (decimal)cfg.BeepVolume, 0.1m, 1);
        beepPanel.Controls.Add(_beepBox);
        beepPanel.Controls.Add(new Label { Text = "volume:", AutoSize = true, Padding = new Padding(8, 5, 0, 0) });
        beepPanel.Controls.Add(_beepVolumeBox);
        AddRow(grid, "Feedback sonoro", beepPanel);

        // -- VAD (modo live/push) --
        _silenceMsBox = NewNumeric(200m, 5000m, cfg.SilenceMs, 50m, 0);
        AddRow(grid, "Pausa p/ frase (ms)", _silenceMsBox,
            "Modos live/push: silêncio que encerra uma frase.");

        _vadThresholdBox = NewNumeric(0.001m, 0.2m, (decimal)cfg.VadThreshold, 0.001m, 3);
        AddRow(grid, "Sensibilidade VAD", _vadThresholdBox,
            "Energia mínima p/ considerar fala. Maior = ignora mais ruído.");

        // -- moldura de foco --
        var borderPanel = NewRowPanel();
        _borderBox = new CheckBox { Text = "Marcar janela em foco", Checked = cfg.FocusBorder, AutoSize = true };
        _borderColorBtn = new Button { Text = "Cor", Width = 60, BackColor = _borderColor, ForeColor = Color.White };
        _borderColorBtn.Click += (_, _) => PickBorderColor();
        _borderThicknessBox = NewNumeric(1m, 40m, cfg.FocusBorderThickness, 1m, 0);
        _borderOpacityBox = NewNumeric(0.1m, 1m, (decimal)cfg.FocusBorderOpacity, 0.1m, 1);
        borderPanel.Controls.Add(_borderBox);
        borderPanel.Controls.Add(_borderColorBtn);
        borderPanel.Controls.Add(new Label { Text = "px:", AutoSize = true, Padding = new Padding(6, 5, 0, 0) });
        borderPanel.Controls.Add(_borderThicknessBox);
        borderPanel.Controls.Add(new Label { Text = "opac.:", AutoSize = true, Padding = new Padding(6, 5, 0, 0) });
        borderPanel.Controls.Add(_borderOpacityBox);
        AddRow(grid, "Moldura de foco", borderPanel,
            "Mostra em qual janela o texto será colado durante a gravação.");

        // -- desempenho --
        _idleUnloadBox = NewNumeric(0m, 240m, cfg.IdleUnloadMinutes, 1m, 0);
        AddRow(grid, "Liberar VRAM após (min)", _idleUnloadBox, "0 = nunca descarregar o modelo.");

        _gpuBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
        _gpuBox.Items.AddRange(new object[] { "auto", "vulkan", "cpu" });
        _gpuBox.SelectedItem = cfg.Gpu is "vulkan" or "cpu" ? cfg.Gpu : "auto";
        AddRow(grid, "Processamento", _gpuBox);

        // -- modelo --
        var modelPanel = NewRowPanel();
        _modelPathBox = new TextBox { Width = 270, Text = Config.LoadRaw().modelPath ?? cfg.ModelPath };
        var browseBtn = new Button { Text = "...", Width = 32 };
        browseBtn.Click += (_, _) => BrowseModel();
        modelPanel.Controls.Add(_modelPathBox);
        modelPanel.Controls.Add(browseBtn);
        AddRow(grid, "Modelo Whisper (.bin)", modelPanel);

        // -- botoes --
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

        Controls.Add(grid);
        Controls.Add(buttons);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        FormClosed += (_, _) => StopCapture();
    }

    // ---- captura de tecla ----
    private void ToggleCapture()
    {
        if (_capture != null) { StopCapture(); return; }

        if (_mainHotkey != null) _mainHotkey.Suspended = true;
        _capture = HotkeyListener.CreateDiscovery();
        _capture.KeyDiscovered += OnKeyCaptured;
        _capture.Start();
        _hotkeyBox.Text = "pressione uma tecla...";
        _captureBtn.Text = "Cancelar";
    }

    private void OnKeyCaptured(int vk)
    {
        if (vk == 0x1B) { RestoreHotkeyDisplay(); StopCapture(); return; } // Esc cancela
        _hotkeyValue = HotkeyJsonValue(vk);
        _hotkeyBox.Text = Config.NameForVk(vk);
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
        if (_hotkeyBox.Text == "pressione uma tecla...") RestoreHotkeyDisplay();
    }

    private void RestoreHotkeyDisplay()
        => _hotkeyBox.Text = _hotkeyValue.Equals("discover", StringComparison.OrdinalIgnoreCase)
            ? "discover"
            : Config.ResolveKey(_hotkeyValue).name;

    /// <summary>Valor pro JSON: nome conhecido ("F15") ou hex ("0x7E") p/ tecla sem nome.</summary>
    private static string HotkeyJsonValue(int vk)
    {
        var name = Config.NameForVk(vk);
        return name.StartsWith("VK_0x") ? $"0x{vk:X2}" : name;
    }

    // ---- demais campos ----
    private void PickBorderColor()
    {
        using var dlg = new ColorDialog { Color = _borderColor, FullOpen = true };
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _borderColor = dlg.Color;
            _borderColorBtn.BackColor = _borderColor;
        }
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
        try
        {
            // preserva campos que a tela nao edita (sons customizados)
            var existing = Config.LoadRaw();
            Config.SaveRaw(new Config.RawConfig
            {
                modelPath = _modelPathBox.Text.Trim(),
                language = string.IsNullOrWhiteSpace(_languageBox.Text) ? "pt" : _languageBox.Text.Trim(),
                hotkey = _hotkeyValue,
                mode = Modes[Math.Max(0, _modeBox.SelectedIndex)].Value,
                autoEnter = _autoEnterBox.Checked,
                beep = _beepBox.Checked,
                beepVolume = (float)_beepVolumeBox.Value,
                startSound = existing.startSound,
                stopSound = existing.stopSound,
                silenceMs = (int)_silenceMsBox.Value,
                vadThreshold = (float)_vadThresholdBox.Value,
                idleUnloadMinutes = (int)_idleUnloadBox.Value,
                gpu = (string)_gpuBox.SelectedItem!,
                focusBorder = _borderBox.Checked,
                focusBorderColor = $"#{_borderColor.R:X2}{_borderColor.G:X2}{_borderColor.B:X2}",
                focusBorderThickness = (int)_borderThicknessBox.Value,
                focusBorderOpacity = (float)_borderOpacityBox.Value,
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

    // ---- helpers de layout ----
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
            MaximumSize = new Size(320, 0),
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(2, 2, 0, 0),
        });
        grid.Controls.Add(stack);
    }

    private static Color ParseColorSafe(string html)
    {
        try { return ColorTranslator.FromHtml(html); }
        catch { return Color.FromArgb(0xE8, 0x11, 0x23); }
    }
}
