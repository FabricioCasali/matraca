using System.Text.Json;

namespace Matraca;

/// <summary>
/// Guarda as ultimas transcricoes em %LOCALAPPDATA%\Matraca\history.json.
///
/// ATENCAO A' PRIVACIDADE: isto grava em disco, em texto puro, tudo o que foi ditado.
/// Quem nao quiser deve desligar (historyEnabled=false) — e a tela de historico tem
/// "Limpar tudo", que apaga a lista e o arquivo.
/// </summary>
internal sealed class DictationHistory
{
    public sealed record Entry(DateTime At, string Text);

    private readonly List<Entry> _items = new();
    private readonly object _gate = new();
    private readonly int _max;
    private readonly string _path;

    public DictationHistory(int maxItems)
    {
        _max = Math.Clamp(maxItems, 1, 5000);
        _path = AppPaths.Current().HistoryFile;
        Load();
    }

    /// <summary>Mais recente primeiro.</summary>
    public List<Entry> Snapshot()
    {
        lock (_gate) return new List<Entry>(_items);
    }

    public void Add(string text)
    {
        text = (text ?? "").Trim();
        if (text.Length == 0) return;

        lock (_gate)
        {
            _items.Insert(0, new Entry(DateTime.Now, text));
            if (_items.Count > _max) _items.RemoveRange(_max, _items.Count - _max);
            Save();
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _items.Clear();
            try { if (File.Exists(_path)) File.Delete(_path); }
            catch (Exception ex) { Logger.Warn("Falha ao apagar o historico: " + ex.Message); }
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var loaded = JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(_path));
            if (loaded == null) return;
            _items.AddRange(loaded.Take(_max));
        }
        catch (Exception ex) { Logger.Warn("Falha ao ler o historico: " + ex.Message); }
    }

    // sempre chamado com _gate seguro
    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path,
                JsonSerializer.Serialize(_items, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { Logger.Warn("Falha ao gravar o historico: " + ex.Message); }
    }
}
