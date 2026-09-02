using System.Windows.Forms;

namespace Matraca;

/// <summary>
/// Tela de historico: lista as transcricoes recentes, mostra o texto completo da selecionada
/// e permite copiar ou recolar.
///
/// O "Colar" precisa de um truque: quando esta tela abre, ELA fica com o foco, entao o alvo
/// original se perde. Guardamos o handle da janela em foco ANTES de exibir e devolvemos o
/// foco pra ela na hora de colar.
/// </summary>
internal sealed class HistoryForm : Form
{
    private readonly DictationHistory _history;
    private readonly TargetToken? _returnTo;
    private readonly ITargetWindow _targetWindow;
    private readonly ITextSink _textSink;

    private readonly ListBox _list;
    private readonly TextBox _detail;
    private List<DictationHistoryEntry> _items;

    public HistoryForm(
        DictationHistory history,
        TargetToken? returnTo,
        ITargetWindow targetWindow,
        ITextSink textSink)
    {
        _history = history;
        _returnTo = returnTo;
        _targetWindow = targetWindow;
        _textSink = textSink;
        _items = history.Snapshot();

        FormClosed += (_, _) =>
        {
            if (_returnTo != null) _targetWindow.Release(_returnTo);
        };

        Text = "Matraca — Histórico de ditados";
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        Size = new Size(700, 480);
        MinimumSize = new Size(520, 360);
        Font = new Font("Segoe UI", 9f);

        _list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        _list.SelectedIndexChanged += (_, _) => ShowSelected();

        _detail = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
        };

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 240,
        };
        split.Panel1.Controls.Add(_list);
        split.Panel2.Controls.Add(_detail);

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Bottom,
            AutoSize = true,
            Padding = new Padding(8),
        };
        var closeBtn = new Button { Text = "Fechar", AutoSize = true, DialogResult = DialogResult.Cancel };
        var clearBtn = new Button { Text = "Limpar tudo", AutoSize = true };
        var copyBtn = new Button { Text = "Copiar", AutoSize = true };
        var pasteBtn = new Button { Text = "Colar na janela anterior", AutoSize = true };
        clearBtn.Click += (_, _) => ClearAll();
        copyBtn.Click += (_, _) => CopySelected();
        pasteBtn.Click += async (_, _) => await PasteSelected();
        buttons.Controls.Add(closeBtn);
        buttons.Controls.Add(clearBtn);
        buttons.Controls.Add(copyBtn);
        buttons.Controls.Add(pasteBtn);
        CancelButton = closeBtn;

        Controls.Add(split);
        Controls.Add(buttons);

        Refill();
    }

    private void Refill()
    {
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var e in _items)
        {
            var oneLine = e.Text.Replace('\r', ' ').Replace('\n', ' ');
            if (oneLine.Length > 90) oneLine = oneLine[..87] + "...";
            _list.Items.Add($"{e.At:dd/MM HH:mm}  {oneLine}");
        }
        _list.EndUpdate();

        if (_list.Items.Count > 0) _list.SelectedIndex = 0;
        else _detail.Text = "(sem ditados guardados)";
    }

    private void ShowSelected()
    {
        int i = _list.SelectedIndex;
        _detail.Text = i >= 0 && i < _items.Count ? _items[i].Text : "";
    }

    private string? Selected()
    {
        int i = _list.SelectedIndex;
        return i >= 0 && i < _items.Count ? _items[i].Text : null;
    }

    private void CopySelected()
    {
        var text = Selected();
        if (text == null) return;
        try { Clipboard.SetText(text); }
        catch (Exception ex)
        {
            Logger.Warn("Falha ao copiar do historico: " + ex.Message);
            MessageBox.Show(this, "Não consegui copiar: " + ex.Message, "Matraca",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task PasteSelected()
    {
        var text = Selected();
        if (text == null) return;

        if (_returnTo == null || !_targetWindow.IsAlive(_returnTo))
        {
            MessageBox.Show(this,
                "A janela que estava em foco não existe mais. Use Copiar e cole você mesmo.",
                "Matraca", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // fecha primeiro: enquanto esta tela existir, ela e' quem tem o foco
        Close();
        await _textSink.DeliverAsync(new TextDeliveryRequest(
            text,
            false,
            TextDeliveryMethod.TargetWithFocus,
            _returnTo));
    }

    private void ClearAll()
    {
        if (MessageBox.Show(this, "Apagar todo o histórico de ditados?", "Matraca",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

        _history.Clear();
        _items = _history.Snapshot();
        Refill();
    }
}
