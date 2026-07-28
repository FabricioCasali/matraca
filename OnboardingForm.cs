using System.Windows.Forms;

namespace Matraca;

/// <summary>
/// Tela de primeiro uso: aparece quando nao ha' um modelo Whisper valido configurado.
/// Resolve as duas coisas sem as quais o app nao funciona — o modelo e a tecla de atalho —
/// e grava direto no appsettings.json do usuario.
/// </summary>
internal sealed class OnboardingForm : Form
{
    private readonly ComboBox _modelBox;
    private readonly Button _downloadBtn;
    private readonly Button _browseBtn;
    private readonly ProgressBar _progress;
    private readonly Label _status;
    private readonly TextBox _hotkeyBox;
    private readonly Button _captureBtn;
    private readonly Button _finishBtn;

    private HotkeyListener? _capture;
    private CancellationTokenSource? _cts;
    private string _modelPath = "";
    private string _hotkeyValue = "";

    public OnboardingForm()
    {
        Text = "Matraca — primeiro uso";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9f);
        ClientSize = new Size(560, 400);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            Padding = new Padding(16),
            AutoSize = true,
        };

        root.Controls.Add(new Label
        {
            Text = "Bem-vindo ao Matraca!\n\n"
                 + "O Matraca transcreve sua voz localmente, sem mandar áudio pra lugar nenhum. "
                 + "Pra isso ele precisa de um modelo Whisper no seu computador.",
            AutoSize = true,
            MaximumSize = new Size(510, 0),
            Margin = new Padding(0, 0, 0, 12),
        });

        // ---- 1) modelo ----
        root.Controls.Add(Header("1. Modelo de transcrição"));

        _modelBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 500 };
        foreach (var m in ModelDownloader.Catalog) _modelBox.Items.Add(m.Label);
        _modelBox.SelectedIndex = 1;   // "small": baixa rápido e funciona sem GPU boa
        root.Controls.Add(_modelBox);

        var modelButtons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 6, 0, 0) };
        _downloadBtn = new Button { Text = "Baixar", AutoSize = true };
        _downloadBtn.Click += async (_, _) => await DownloadAsync();
        _browseBtn = new Button { Text = "Já tenho um modelo...", AutoSize = true };
        _browseBtn.Click += (_, _) => BrowseExisting();
        modelButtons.Controls.Add(_downloadBtn);
        modelButtons.Controls.Add(_browseBtn);
        root.Controls.Add(modelButtons);

        _progress = new ProgressBar { Width = 500, Height = 16, Visible = false, Margin = new Padding(0, 6, 0, 0) };
        root.Controls.Add(_progress);

        _status = new Label
        {
            Text = "Nenhum modelo escolhido ainda.",
            AutoSize = true,
            MaximumSize = new Size(510, 0),
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 4, 0, 12),
        };
        root.Controls.Add(_status);

        // ---- 2) atalho ----
        root.Controls.Add(Header("2. Tecla de atalho"));

        var hotkeyPanel = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        _hotkeyBox = new TextBox { ReadOnly = true, Width = 200, Text = "(nenhuma)" };
        _captureBtn = new Button { Text = "Capturar...", AutoSize = true };
        _captureBtn.Click += (_, _) => ToggleCapture();
        hotkeyPanel.Controls.Add(_hotkeyBox);
        hotkeyPanel.Controls.Add(_captureBtn);
        root.Controls.Add(hotkeyPanel);

        root.Controls.Add(new Label
        {
            Text = "Clique em Capturar e aperte a tecla que vai ligar o ditado. Use uma tecla "
                 + "dedicada (F13–F24, tecla de mídia) ou um combo como Ctrl+Alt+D — a tecla "
                 + "escolhida deixa de chegar nos outros programas.",
            AutoSize = true,
            MaximumSize = new Size(510, 0),
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 4, 0, 12),
        });

        // ---- botoes ----
        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Bottom,
            AutoSize = true,
            Padding = new Padding(0, 8, 16, 12),
        };
        var laterBtn = new Button { Text = "Configurar depois", AutoSize = true, DialogResult = DialogResult.Cancel };
        _finishBtn = new Button { Text = "Concluir", AutoSize = true, Enabled = false };
        _finishBtn.Click += (_, _) => Finish();
        buttons.Controls.Add(laterBtn);
        buttons.Controls.Add(_finishBtn);
        CancelButton = laterBtn;

        Controls.Add(root);
        Controls.Add(buttons);
        FormClosing += (_, e) =>
        {
            StopCapture();
            // nao deixa fechar no meio de um download: o .part ficaria orfao
            if (_cts is { IsCancellationRequested: false })
            {
                _cts.Cancel();
                e.Cancel = false;
            }
        };

        OfferAlreadyDownloaded();
    }

    private static Label Header(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = new Font("Segoe UI", 10f, FontStyle.Bold),
        Margin = new Padding(0, 0, 0, 6),
    };

    /// <summary>Se ja' existe um .bin baixado, aproveita em vez de mandar baixar de novo.</summary>
    private void OfferAlreadyDownloaded()
    {
        var existing = ModelDownloader.Existing().FirstOrDefault();
        if (existing == null) return;
        _modelPath = existing;
        _status.Text = $"Usando o modelo já baixado: {Path.GetFileName(existing)}";
        UpdateFinishState();
    }

    private async Task DownloadAsync()
    {
        var model = ModelDownloader.Catalog[Math.Max(0, _modelBox.SelectedIndex)];
        var target = ModelDownloader.PathFor(model);

        if (File.Exists(target))
        {
            _modelPath = target;
            _status.Text = $"Esse modelo já está baixado: {target}";
            UpdateFinishState();
            return;
        }

        _cts = new CancellationTokenSource();
        _downloadBtn.Enabled = false;
        _browseBtn.Enabled = false;
        _modelBox.Enabled = false;
        _progress.Visible = true;
        _progress.Value = 0;

        var progress = new Progress<(long done, long total)>(p =>
        {
            if (p.total <= 0) return;
            _progress.Value = (int)Math.Clamp(p.done * 100 / p.total, 0, 100);
            _status.Text = $"Baixando... {ModelDownloader.HumanSize(p.done)} de "
                         + $"{ModelDownloader.HumanSize(p.total)}";
        });

        try
        {
            _modelPath = await ModelDownloader.DownloadAsync(model, progress, _cts.Token);
            _status.Text = $"Modelo pronto: {_modelPath}";
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Download cancelado.";
        }
        catch (Exception ex)
        {
            Logger.Error("Falha ao baixar o modelo", ex);
            _status.Text = "Falha no download: " + ex.Message;
            MessageBox.Show(this,
                "Não consegui baixar o modelo:\n\n" + ex.Message
              + "\n\nVocê pode tentar de novo, ou baixar manualmente e usar \"Já tenho um modelo\".",
                "Matraca", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            _downloadBtn.Enabled = true;
            _browseBtn.Enabled = true;
            _modelBox.Enabled = true;
            _progress.Visible = false;
            UpdateFinishState();
        }
    }

    private void BrowseExisting()
    {
        using var dlg = new OpenFileDialog
        {
            Filter = "Modelo Whisper ggml (*.bin)|*.bin|Todos (*.*)|*.*",
            Title = "Escolha o arquivo .bin do modelo Whisper",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        _modelPath = dlg.FileName;
        _status.Text = "Modelo escolhido: " + _modelPath;
        UpdateFinishState();
    }

    // ---- captura de tecla ----
    private void ToggleCapture()
    {
        if (_capture != null) { StopCapture(); return; }
        _capture = HotkeyListener.CreateDiscovery();
        _capture.KeyDiscovered += OnKeyCaptured;
        _capture.Start();
        _hotkeyBox.Text = "pressione uma tecla...";
        _captureBtn.Text = "Cancelar";
    }

    private void OnKeyCaptured(int vk, KeyMods mods)
    {
        if (vk == 0x1B && mods == KeyMods.None) { StopCapture(); return; }   // Esc cancela

        var combo = Config.FormatHotkey(vk, mods);
        if (Config.ParseHotkey(combo).vk == 0)
        {
            MessageBox.Show(this,
                $"'{combo}' não serve como atalho: teclas de digitação sozinhas parariam de "
              + "funcionar no sistema inteiro. Junte um modificador (ex.: Ctrl+Alt+"
              + Config.NameForVk(vk) + ") ou use uma tecla dedicada como F13–F24.",
                "Matraca", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            StopCapture();
            return;
        }

        _hotkeyValue = combo;
        _hotkeyBox.Text = combo;
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
        _captureBtn.Text = "Capturar...";
        if (_hotkeyBox.Text == "pressione uma tecla...")
            _hotkeyBox.Text = _hotkeyValue.Length == 0 ? "(nenhuma)" : _hotkeyValue;
        UpdateFinishState();
    }

    private void UpdateFinishState()
        => _finishBtn.Enabled = _modelPath.Length > 0 && _hotkeyValue.Length > 0;

    private void Finish()
    {
        try
        {
            // preserva o que ja' existir no arquivo; so' preenche o que esta tela resolve
            var raw = Config.LoadRaw();
            raw.modelPath = _modelPath;
            raw.hotkey = _hotkeyValue;
            Config.SaveRaw(raw);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            Logger.Error("Falha ao salvar a configuracao inicial", ex);
            MessageBox.Show(this, "Não consegui salvar: " + ex.Message, "Matraca",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
