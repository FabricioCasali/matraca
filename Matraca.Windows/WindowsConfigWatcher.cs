using System.Text.Json;

namespace Matraca;

internal sealed class WindowsConfigWatcher : IDisposable
{
    private const int DebounceMilliseconds = 250;
    private const int RetryMilliseconds = 100;
    private const int MaximumAttempts = 5;

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly object _gate = new();
    private readonly FileSystemWatcher _watcher;
    private readonly System.Threading.Timer _debounce;
    private int _disposed;

    public WindowsConfigWatcher()
    {
        Directory.CreateDirectory(WindowsConfig.Paths.DataDirectory);
        _debounce = new System.Threading.Timer(Reload, null, Timeout.Infinite, Timeout.Infinite);
        _watcher = new FileSystemWatcher(
            WindowsConfig.Paths.DataDirectory,
            Path.GetFileName(WindowsConfig.Paths.ConfigFile))
        {
            NotifyFilter = NotifyFilters.LastWrite |
                           NotifyFilters.FileName |
                           NotifyFilters.Size |
                           NotifyFilters.CreationTime,
        };
        _watcher.Changed += OnChanged;
        _watcher.Created += OnChanged;
        _watcher.Deleted += OnChanged;
        _watcher.Renamed += OnChanged;
        _watcher.Error += OnError;
        _watcher.EnableRaisingEvents = true;
    }

    public event Action<Config>? Changed;

    private void OnChanged(object sender, FileSystemEventArgs args) => ScheduleReload();

    private void OnError(object sender, ErrorEventArgs args)
    {
        Logger.Warn($"Falha ao observar appsettings.json; tentando recarregar: {args.GetException().Message}");
        ScheduleReload();
    }

    private void ScheduleReload()
    {
        lock (_gate)
        {
            if (_disposed != 0) return;
            _debounce.Change(DebounceMilliseconds, Timeout.Infinite);
        }
    }

    private void Reload(object? state)
    {
        for (int attempt = 1; attempt <= MaximumAttempts; attempt++)
        {
            if (Volatile.Read(ref _disposed) != 0) return;

            try
            {
                Config config = ReadConfig();
                if (Volatile.Read(ref _disposed) == 0) Changed?.Invoke(config);
                return;
            }
            catch (Exception exception) when (IsRetryable(exception) && attempt < MaximumAttempts)
            {
                Thread.Sleep(RetryMilliseconds);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Logger.Warn($"Falha ao recarregar appsettings.json: {exception.Message}");
                return;
            }
            catch (Exception exception)
            {
                Logger.Warn($"appsettings.json invalido; mantendo a configuracao atual: {exception.Message}");
                return;
            }
        }
    }

    private static Config ReadConfig()
    {
        using var stream = new FileStream(
            WindowsConfig.Paths.ConfigFile,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        RawConfig raw = JsonSerializer.Deserialize<RawConfig>(stream, ReadOptions)
            ?? throw new JsonException("appsettings.json nao contem uma configuracao.");
        return Config.FromRaw(raw, WindowsHotkeyTranslator.ParseCompatibility, Logger.Warn, WindowsConfig.Paths);
    }

    private static bool IsRetryable(Exception exception)
        => exception is IOException or UnauthorizedAccessException or JsonException;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        _watcher.EnableRaisingEvents = false;
        _watcher.Changed -= OnChanged;
        _watcher.Created -= OnChanged;
        _watcher.Deleted -= OnChanged;
        _watcher.Renamed -= OnChanged;
        _watcher.Error -= OnError;
        _watcher.Dispose();
        lock (_gate)
        {
            _debounce.Dispose();
            Changed = null;
        }
    }
}
