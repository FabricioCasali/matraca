using System.Text.Json;

namespace Matraca.Core;

public sealed class AiUsageLedger
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private readonly List<AiUsageBucket> _items = new();
    private readonly object _gate = new();
    private readonly string _path;
    private readonly Action<string, string> _replaceFile;

    public AiUsageLedger(AppPaths paths, Action<string, string>? replaceFile = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _path = paths.AiUsageFile;
        _replaceFile = replaceFile ?? ((temporary, destination) =>
            File.Move(temporary, destination, overwrite: true));
        Load();
    }

    public List<AiUsageBucket> Snapshot()
    {
        lock (_gate) return _items.Select(item => item with
        {
            PricingVersions = item.PricingVersions.ToArray(),
        }).ToList();
    }

    public bool Add(TextReviewUsage usage)
    {
        ArgumentNullException.ThrowIfNull(usage);
        lock (_gate)
        {
            DateOnly day = DateOnly.FromDateTime(usage.At);
            int index = _items.FindIndex(item =>
                item.Day == day
                && item.Provider == usage.Provider
                && item.Model == usage.Model);
            AiUsageBucket? previous = index >= 0 ? _items[index] : null;
            var updated = new AiUsageBucket(
                day,
                usage.Provider,
                usage.Model,
                (previous?.Requests ?? 0) + 1,
                (previous == null ? 0 : previous.PromptTokens) + usage.PromptTokens,
                (previous == null ? 0 : previous.PromptCacheHitTokens) + usage.PromptCacheHitTokens,
                (previous == null ? 0 : previous.PromptCacheMissTokens) + usage.PromptCacheMissTokens,
                (previous == null ? 0 : previous.CompletionTokens) + usage.CompletionTokens,
                (previous == null ? 0 : previous.ReasoningTokens) + usage.ReasoningTokens,
                (previous == null ? 0 : previous.TotalTokens) + usage.TotalTokens,
                // A partial cost is a known subtotal, never proof of full coverage.
                previous?.EstimatedCostUsd == null && usage.EstimatedCostUsd == null
                    ? null : (previous?.EstimatedCostUsd ?? 0) + (usage.EstimatedCostUsd ?? 0),
                (previous == null ? 0 : previous.PricedRequests) + (usage.EstimatedCostUsd.HasValue ? 1 : 0),
                (previous == null ? 0 : previous.UnpricedRequests) + (usage.EstimatedCostUsd.HasValue ? 0 : 1))
            {
                PricingVersions = (previous?.PricingVersions ?? [])
                    .Concat(usage.PricingVersion is { Length: > 0 } version ? [version] : [])
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(version => version, StringComparer.Ordinal)
                    .ToArray(),
            };
            if (index >= 0) _items[index] = updated;
            else _items.Add(updated);
            if (Save()) return true;
            if (previous != null) _items[index] = previous;
            else _items.RemoveAt(_items.Count - 1);
            return false;
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var loaded = JsonSerializer.Deserialize<List<AiUsageBucket>>(File.ReadAllText(_path));
            if (loaded != null) _items.AddRange(loaded.Select(item => item with
            {
                PricingVersions = item.PricingVersions ?? [],
            }));
        }
        catch (Exception exception)
        {
            Logger.Warn("Falha ao ler consumo local de IA: " + exception.Message);
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
        catch (Exception exception)
        {
            Logger.Warn("Falha ao gravar consumo local de IA: " + exception.Message);
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
