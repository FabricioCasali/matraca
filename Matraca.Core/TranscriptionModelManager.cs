namespace Matraca.Core;

public sealed class TranscriptionModelManager : IDisposable
{
    private static readonly TimeSpan DefaultIdleCheckInterval = TimeSpan.FromSeconds(30);

    private readonly object _gate = new();
    private readonly Func<Config, CancellationToken, Task<TranscriptionModel>> _factory;
    private readonly Func<long> _clock;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly HashSet<TranscriptionModel> _retired = new();
    private readonly Timer? _idleTimer;

    private Config _config;
    private TranscriptionModel? _model;
    private Task<TranscriptionModel?>? _loadTask;
    private long _loadVersion;
    private long _version;
    private long _lastActivity;
    private int _activeUsers;
    private int _disposed;

    public TranscriptionModelManager(
        Config config,
        Func<Config, CancellationToken, Task<TranscriptionModel>>? factory = null,
        Func<long>? clock = null,
        bool startIdleTimer = true,
        TimeSpan? idleCheckInterval = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _factory = factory ?? CreateDefaultAsync;
        _clock = clock ?? (() => Environment.TickCount64);
        _lastActivity = _clock();
        if (startIdleTimer)
        {
            var interval = idleCheckInterval ?? DefaultIdleCheckInterval;
            _idleTimer = new Timer(_ => UnloadIfIdle(), null, interval, interval);
        }
    }

    public event Action<TranscriptionModelState>? StateChanged;

    public TranscriptionModelState State { get; private set; } = TranscriptionModelState.Unloaded;
    public Exception? LastError { get; private set; }
    public long Version { get { lock (_gate) return _version; } }
    public bool IsLoaded { get { lock (_gate) return _model != null; } }
    public bool IsLoading { get { lock (_gate) return _loadTask != null; } }
    public bool IsInUse { get { lock (_gate) return _activeUsers > 0; } }

    public Task<TranscriptionModel?> PreloadAsync(CancellationToken cancellationToken = default)
        => GetModelAsync(cancellationToken);

    public Task<TranscriptionModel?> GetModelAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            _lastActivity = _clock();
            if (_model != null) return Task.FromResult<TranscriptionModel?>(_model);
            if (_loadTask != null && _loadVersion == _version)
                return WaitForLoadAsync(_loadTask, cancellationToken);
            return WaitForLoadAsync(StartLoadLocked(_version, _config), cancellationToken);
        }
    }

    public Task<TranscriptionModel?> ReloadAsync(
        Config config,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        Task<TranscriptionModel?> load;
        lock (_gate)
        {
            ThrowIfDisposed();
            _version++;
            _lastActivity = _clock();
            load = StartLoadLocked(_version, config);
        }
        return WaitForLoadAsync(load, cancellationToken);
    }

    public void SetIdleUnloadMinutes(int minutes)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            _config = CopyWithIdleUnload(_config, Math.Clamp(minutes, 0, 240));
            _lastActivity = _clock();
        }
    }

    public void BeginUse()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            _activeUsers++;
            _lastActivity = _clock();
        }
    }

    public void EndUse()
    {
        TranscriptionModel[] dispose;
        lock (_gate)
        {
            if (_activeUsers > 0) _activeUsers--;
            _lastActivity = _clock();
            if (_activeUsers != 0 || _retired.Count == 0) return;
            dispose = _retired.ToArray();
            _retired.Clear();
        }
        DisposeModels(dispose);
    }

    public void Touch()
    {
        lock (_gate)
        {
            if (Volatile.Read(ref _disposed) == 0) _lastActivity = _clock();
        }
    }

    public bool UnloadIfIdle()
    {
        TranscriptionModel? dispose = null;
        bool changed = false;
        lock (_gate)
        {
            if (Volatile.Read(ref _disposed) != 0 || _config.IdleUnloadMinutes <= 0)
                return false;
            long idleMilliseconds = _clock() - _lastActivity;
            if (idleMilliseconds < (long)_config.IdleUnloadMinutes * 60_000L
                || _activeUsers != 0 || _loadTask != null || _model == null)
                return false;

            dispose = _model;
            _model = null;
            LastError = null;
            changed = SetStateLocked(TranscriptionModelState.Unloaded);
        }
        dispose.Dispose();
        if (changed) StateChanged?.Invoke(TranscriptionModelState.Unloaded);
        Logger.Info($"Modelo descarregado por inatividade ({_config.IdleUnloadMinutes} min). VRAM liberada.");
        return true;
    }

    public void Unload()
    {
        TranscriptionModel? dispose;
        bool changed;
        lock (_gate)
        {
            ThrowIfDisposed();
            _version++;
            _loadTask = null;
            dispose = _model;
            _model = null;
            LastError = null;
            changed = SetStateLocked(TranscriptionModelState.Unloaded);
        }
        dispose?.Dispose();
        if (changed) StateChanged?.Invoke(TranscriptionModelState.Unloaded);
    }

    private Task<TranscriptionModel?> StartLoadLocked(long version, Config config)
    {
        _loadVersion = version;
        LastError = null;
        bool changed = SetStateLocked(TranscriptionModelState.Loading);
        var completion = new TaskCompletionSource<TranscriptionModel?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _loadTask = completion.Task;
        _ = CompleteLoadAsync(version, config, completion);
        if (changed) _ = Task.Run(() => NotifyLoadingIfCurrent(version));
        return completion.Task;
    }

    private void NotifyLoadingIfCurrent(long version)
    {
        lock (_gate)
        {
            if (version != _version || State != TranscriptionModelState.Loading) return;
            StateChanged?.Invoke(TranscriptionModelState.Loading);
        }
    }

    private async Task CompleteLoadAsync(
        long version,
        Config config,
        TaskCompletionSource<TranscriptionModel?> completion)
    {
        try { completion.TrySetResult(await LoadAsync(version, config).ConfigureAwait(false)); }
        catch (Exception exception) { completion.TrySetException(exception); }
    }

    private async Task<TranscriptionModel?> LoadAsync(long version, Config config)
    {
        TranscriptionModel? created = null;
        TranscriptionModel? replaced = null;
        TranscriptionModelState? notification = null;
        try
        {
            created = await _factory(config, _shutdown.Token).ConfigureAwait(false);
            lock (_gate)
            {
                if (Volatile.Read(ref _disposed) != 0 || version != _version)
                    return null;

                replaced = _model;
                _model = created;
                _config = config;
                created = null;
                if (replaced != null && _activeUsers > 0)
                {
                    _retired.Add(replaced);
                    replaced = null;
                }
                LastError = null;
                if (SetStateLocked(TranscriptionModelState.Ready))
                    notification = TranscriptionModelState.Ready;
                return _model;
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception exception)
        {
            lock (_gate)
            {
                if (Volatile.Read(ref _disposed) == 0 && version == _version)
                {
                    LastError = exception;
                    var next = _model == null
                        ? TranscriptionModelState.Failed
                        : TranscriptionModelState.Ready;
                    if (SetStateLocked(next)) notification = next;
                }
            }
            Logger.Error("Falha ao carregar o modelo", exception);
            return null;
        }
        finally
        {
            created?.Dispose();
            replaced?.Dispose();
            lock (_gate)
            {
                if (_loadVersion == version) _loadTask = null;
            }
            lock (_gate)
            {
                if (notification != null
                    && version == _version
                    && State == notification.Value)
                    StateChanged?.Invoke(notification.Value);
            }
        }
    }

    private static async Task<TranscriptionModel?> WaitForLoadAsync(
        Task<TranscriptionModel?> load,
        CancellationToken cancellationToken)
        => await load.WaitAsync(cancellationToken).ConfigureAwait(false);

    private bool SetStateLocked(TranscriptionModelState state)
    {
        if (State == state) return false;
        State = state;
        return true;
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

    private static Task<TranscriptionModel> CreateDefaultAsync(
        Config config,
        CancellationToken cancellationToken)
        => Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var transcriber = new Transcriber(config.ModelPath, config.Language, config.Vocabulary);
            return new TranscriptionModel(
                transcriber.TranscribeAsync,
                transcriber.Dispose);
        }, cancellationToken);

    private static Config CopyWithIdleUnload(Config source, int minutes) => new()
    {
        ModelPath = source.ModelPath,
        Language = source.Language,
        DiscoverMode = source.DiscoverMode,
        Hotkey = source.Hotkey,
        PinHotkey = source.PinHotkey,
        PinDelivery = source.PinDelivery,
        Mode = source.Mode,
        AutoEnter = source.AutoEnter,
        Beep = source.Beep,
        BeepVolume = source.BeepVolume,
        StartSound = source.StartSound,
        StopSound = source.StopSound,
        SilenceMs = source.SilenceMs,
        VadThreshold = source.VadThreshold,
        MicSensitivity = source.MicSensitivity,
        PhraseMaxSeconds = source.PhraseMaxSeconds,
        InputDevice = source.InputDevice,
        Vocabulary = source.Vocabulary,
        History = source.History,
        HistoryMaxItems = source.HistoryMaxItems,
        PostProcess = source.PostProcess,
        PostProcessProvider = source.PostProcessProvider,
        PostProcessEndpoint = source.PostProcessEndpoint,
        PostProcessModel = source.PostProcessModel,
        PostProcessApiKey = source.PostProcessApiKey,
        PostProcessPrompt = source.PostProcessPrompt,
        PostProcessTimeoutMs = source.PostProcessTimeoutMs,
        IdleUnloadMinutes = minutes,
        Gpu = source.Gpu,
        FocusBorder = source.FocusBorder,
        FocusBorderColor = source.FocusBorderColor,
        FocusBorderColorBusy = source.FocusBorderColorBusy,
        FocusBorderColorPinned = source.FocusBorderColorPinned,
        FocusBorderThickness = source.FocusBorderThickness,
        FocusBorderOpacity = source.FocusBorderOpacity,
        PasteMethod = source.PasteMethod,
    };

    private static void DisposeModels(IEnumerable<TranscriptionModel> models)
    {
        foreach (var model in models)
        {
            try { model.Dispose(); }
            catch { }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _idleTimer?.Dispose();
        _shutdown.Cancel();
        TranscriptionModel[] models;
        lock (_gate)
        {
            _version++;
            models = _retired.Append(_model).OfType<TranscriptionModel>().Distinct().ToArray();
            _retired.Clear();
            _model = null;
            State = TranscriptionModelState.Disposed;
        }
        DisposeModels(models);
        _shutdown.Dispose();
        StateChanged?.Invoke(TranscriptionModelState.Disposed);
    }
}
