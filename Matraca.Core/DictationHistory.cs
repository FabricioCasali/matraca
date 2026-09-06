using System.Text.Json;

namespace Matraca.Core;

public sealed class DictationHistory
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private readonly List<DictationHistoryEntry> _items = new();
    private readonly object _gate = new();
    private readonly string _path;
    private readonly Func<DateTime> _clock;
    private readonly Action<string, string> _replaceFile;
    private int _max;

    public DictationHistory(
        AppPaths paths,
        int maxItems,
        Func<DateTime>? clock = null,
        Action<string, string>? replaceFile = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _max = Math.Clamp(maxItems, 1, 5000);
        _path = paths.HistoryFile;
        _clock = clock ?? (() => DateTime.Now);
        _replaceFile = replaceFile ?? ((temporary, destination) =>
            File.Move(temporary, destination, overwrite: true));
        Load();
    }

    public List<DictationHistoryEntry> Snapshot()
    {
        lock (_gate) return new List<DictationHistoryEntry>(_items);
    }

    public void Add(string? text, TextReviewUsage? reviewUsage = null)
    {
        text = (text ?? "").Trim();
        if (text.Length == 0) return;

        lock (_gate)
        {
            _items.Insert(0, new DictationHistoryEntry(_clock(), text, reviewUsage));
            if (_items.Count > _max) _items.RemoveRange(_max, _items.Count - _max);
            Save();
        }
    }

    public void SetMaximumItems(int maxItems)
    {
        lock (_gate)
        {
            _max = Math.Clamp(maxItems, 1, 5000);
            if (_items.Count <= _max) return;
            _items.RemoveRange(_max, _items.Count - _max);
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

    public bool Remove(DateTime at, string text)
    {
        lock (_gate)
        {
            int index = _items.FindIndex(entry => entry.At == at && entry.Text == text);
            if (index < 0) return false;
            DictationHistoryEntry removed = _items[index];
            _items.RemoveAt(index);
            if (Save()) return true;
            _items.Insert(index, removed);
            return false;
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

    private bool Save()
    {
        string? temporaryPath = null;
        try
        {
            string directory = Path.GetDirectoryName(_path)!;
            Directory.CreateDirectory(directory);
            temporaryPath = Path.Combine(
                directory,
                $".{Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp");
            using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, _items, WriteOptions);
                stream.Flush(flushToDisk: true);
            }
            _replaceFile(temporaryPath, _path);
            temporaryPath = null;
            return true;
        }
        catch (Exception ex)
        {
            Logger.Warn("Falha ao gravar o historico: " + ex.Message);
            return false;
        }
        finally
        {
            if (temporaryPath != null)
            {
                try { File.Delete(temporaryPath); } catch { }
            }
        }
    }
}
