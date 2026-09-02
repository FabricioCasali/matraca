using System.Text.Json;

namespace Matraca.Core;

public sealed class DictationHistory
{
    private readonly List<DictationHistoryEntry> _items = new();
    private readonly object _gate = new();
    private readonly int _max;
    private readonly string _path;
    private readonly Func<DateTime> _clock;

    public DictationHistory(AppPaths paths, int maxItems, Func<DateTime>? clock = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _max = Math.Clamp(maxItems, 1, 5000);
        _path = paths.HistoryFile;
        _clock = clock ?? (() => DateTime.Now);
        Load();
    }

    public List<DictationHistoryEntry> Snapshot()
    {
        lock (_gate) return new List<DictationHistoryEntry>(_items);
    }

    public void Add(string? text)
    {
        text = (text ?? "").Trim();
        if (text.Length == 0) return;

        lock (_gate)
        {
            _items.Insert(0, new DictationHistoryEntry(_clock(), text));
            if (_items.Count > _max) _items.RemoveRange(_max, _items.Count - _max);
            Save();
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _items.Clear();
            try
            {
                if (File.Exists(_path)) File.Delete(_path);
            }
            catch (Exception ex)
            {
                Logger.Warn("Falha ao apagar o historico: " + ex.Message);
            }
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var loaded = JsonSerializer.Deserialize<List<DictationHistoryEntry>>(File.ReadAllText(_path));
            if (loaded != null) _items.AddRange(loaded.Take(_max));
        }
        catch (Exception ex)
        {
            Logger.Warn("Falha ao ler o historico: " + ex.Message);
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(
                _path,
                JsonSerializer.Serialize(_items, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Logger.Warn("Falha ao gravar o historico: " + ex.Message);
        }
    }
}
