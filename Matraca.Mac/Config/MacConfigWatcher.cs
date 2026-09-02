using Matraca.Core;

namespace Matraca.Mac.Config;

internal sealed class MacConfigWatcher : IDisposable
{
    private readonly object _gate = new();
    private readonly FileSystemWatcher _watcher;
    private Timer? _debounce;
    private int _disposed;

    public MacConfigWatcher()
    {
        Directory.CreateDirectory(MacConfig.Paths.DataDirectory);
        _watcher = new FileSystemWatcher(
            MacConfig.Paths.DataDirectory,
            Path.GetFileName(MacConfig.Paths.ConfigFile))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };
        _watcher.Changed += OnChanged;
        _watcher.Created += OnChanged;
        _watcher.Renamed += OnChanged;
    }

    public event Action<Matraca.Core.Config>? Changed;

    private void OnChanged(object sender, FileSystemEventArgs args)
    {
        lock (_gate)
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            _debounce?.Dispose();
            _debounce = new Timer(_ => Reload(), null, 250, Timeout.Infinite);
        }
    }

    private void Reload()
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        for (int attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                Changed?.Invoke(MacConfig.Load());
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                if (attempt == 5)
                    Logger.Warn($"Falha ao recarregar appsettings.json: {exception.Message}");
                else
                    Thread.Sleep(100);
            }
            catch (Exception exception)
            {
                Logger.Warn($"appsettings.json invalido; mantendo a configuracao atual: {exception.Message}");
                return;
            }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
        lock (_gate)
        {
            _debounce?.Dispose();
            _debounce = null;
        }
    }
}
