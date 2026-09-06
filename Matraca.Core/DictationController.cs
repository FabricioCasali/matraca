namespace Matraca.Core;

public sealed class DictationController : IDisposable
{
    private const int MaximumMuteWindowMilliseconds = 900;
    private const int LifecycleActive = 0;
    private const int LifecycleSuspended = 1;
    private const int LifecycleResuming = 2;

    private readonly object _commandGate = new();
    private readonly object _shutdownGate = new();
    private readonly object _lifecycleGate = new();
    private readonly object _targetGate = new();
    private readonly IAudioCapture _audio;
    private readonly ITargetWindow _targets;
    private readonly IShell _shell;
    private readonly TranscriptionModelManager _models;
    private readonly Func<Config, TextPostProcessor?> _postProcessorFactory;
    private readonly Func<Config, DictationHistory?> _historyFactory;
    private readonly AiUsageLedger? _usageLedger;
    private readonly Action<Action> _dispatch;
    private readonly DeliveryQueue _delivery;
    private readonly CancellationTokenSource _shutdownCancellation = new();
    private readonly List<TextPostProcessor> _retiredPostProcessors = new();
    private readonly Dictionary<TargetToken, int> _targetUseCounts = new();
    private readonly HashSet<TargetToken> _targetsPendingRelease = new();

    private IKeyboardHook _keyboard;
    private Config _config;
    private TextPostProcessor? _postProcessor;
    private DictationHistory? _history;
    private volatile DictationSession? _session;
    private Task _commandTail = Task.CompletedTask;
    private Task? _shutdownTask;
    private Config? _pendingModelReload;
    private TaskCompletionSource? _pendingModelReloadCompletion;
    private TargetToken? _pinnedTarget;
    private string _pinnedTitle = "";
    private HotkeyGesture? _lastDiscovered;
    private IKeyboardHook? _pendingKeyboard;
    private Config? _pendingKeyboardConfig;
    private TaskCompletionSource? _pendingKeyboardCompletion;
    private bool _historyConfigurationPending;
    private bool _dictationKeyDown;
    private bool _announcedReady;
    private bool _warnedLongBeep;
    private int _busy;
    private int _lifecycleState;
    private int _started;
    private int _shuttingDown;
    private int _disposed;

    public DictationController(
        Config config,
        IKeyboardHook keyboard,
        IAudioCapture audio,
        ITextSink textSink,
        ITargetWindow targets,
        IShell shell,
        TranscriptionModelManager models,
        Func<Config, TextPostProcessor?>? postProcessorFactory = null,
        Func<Config, DictationHistory?>? historyFactory = null,
        Action<Action>? dispatch = null,
        Action<Thread>? configureDeliveryThread = null,
        AiUsageLedger? usageLedger = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _keyboard = keyboard ?? throw new ArgumentNullException(nameof(keyboard));
        _audio = audio ?? throw new ArgumentNullException(nameof(audio));
        _targets = targets ?? throw new ArgumentNullException(nameof(targets));
        _shell = shell ?? throw new ArgumentNullException(nameof(shell));
        _models = models ?? throw new ArgumentNullException(nameof(models));
        _postProcessorFactory = postProcessorFactory ?? (_ => null);
        _historyFactory = historyFactory ?? (_ => null);
        _usageLedger = usageLedger;
        _dispatch = dispatch ?? (action => action());
        _delivery = new DeliveryQueue(textSink, configureDeliveryThread);
        _postProcessor = CreatePostProcessor(config);
        _history = CreateHistory(config);
    }

    public DeliveryQueue Delivery => _delivery;
    public DictationHistory? CurrentHistory => _history;
    public AiUsageLedger? UsageLedger => _usageLedger;
    public bool IsSessionActive => _session != null;
    public bool IsBusy => Volatile.Read(ref _busy) != 0;
    public bool IsSuspended => Volatile.Read(ref _lifecycleState) != LifecycleActive;
    public Config CurrentConfig => _config;
    public bool PostProcessingActive => Volatile.Read(ref _postProcessor) != null;
    public TargetToken? PinnedTarget { get { lock (_targetGate) return _pinnedTarget; } }
    public event Action<bool>? DeliveryStarted;
    public event Action<string, TextDeliveryResult, bool>? DeliveryCompleted;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Interlocked.Exchange(ref _started, 1) != 0) return;

        SubscribeKeyboard(_keyboard);
        _models.StateChanged += OnModelStateChanged;
        ConfigureIndicator(_config);
        _keyboard.Start();
        SetIdle();
        Observe(_models.PreloadAsync(_shutdownCancellation.Token));
    }

    public Task HandleDictationKeyAsync(bool pressed)
        => EnqueueCommand(() => HandleDictationKeyCoreAsync(pressed));

    public Task ToggleDictationFromUiAsync(TargetToken? deliveryTarget)
        => EnqueueCommand(() => ToggleDictationFromUiCoreAsync(deliveryTarget));

    public Task TogglePinAsync()
        => EnqueueCommand(TogglePinCoreAsync);

    public async Task ApplyConfigAsync(Config config)
    {
        ArgumentNullException.ThrowIfNull(config);
        TaskCompletionSource? deferred = null;
        await EnqueueCommand(() => ApplyConfigCoreAsync(config, value => deferred = value))
            .ConfigureAwait(false);
        if (deferred != null) await deferred.Task.ConfigureAwait(false);
    }

    public Task ApplyConfigAsync(Config config, IKeyboardHook keyboard)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(keyboard);
        return QueueKeyboardReplacementAsync(keyboard, config);
    }

    public Task ReplaceKeyboardHookAsync(IKeyboardHook keyboard)
    {
        ArgumentNullException.ThrowIfNull(keyboard);
        return QueueKeyboardReplacementAsync(keyboard, config: null);
    }

    public Task SuspendAsync()
    {
        lock (_lifecycleGate)
        {
            if (Volatile.Read(ref _shuttingDown) != 0
                || _lifecycleState == LifecycleSuspended)
                return DrainAsync();

            _lifecycleState = LifecycleSuspended;
            _dictationKeyDown = false;
            try { _keyboard.Suspended = true; }
            catch (Exception exception) { Logger.Error("Falha ao suspender o teclado", exception); }
            try { _session?.Cancellation.Cancel(); }
            catch (ObjectDisposedException) { }
            return EnqueueCommand(SuspendCoreAsync);
        }
    }

    public Task ResumeAsync()
    {
        lock (_lifecycleGate)
        {
            if (Volatile.Read(ref _shuttingDown) != 0
                || _lifecycleState != LifecycleSuspended)
                return DrainAsync();

            _keyboard.Suspended = false;
            _lifecycleState = LifecycleResuming;
            return EnqueueCommand(ResumeCoreAsync);
        }
    }

    private async Task QueueKeyboardReplacementAsync(IKeyboardHook keyboard, Config? config)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool accepted = false;
        await EnqueueCommand(async () =>
        {
            accepted = true;
            if (_pendingKeyboard != null)
            {
                try { _pendingKeyboard.Dispose(); } catch { }
                _pendingKeyboardCompletion?.TrySetException(
                    new InvalidOperationException("Keyboard replacement was superseded."));
            }
            _pendingKeyboard = keyboard;
            _pendingKeyboardConfig = config;
            _pendingKeyboardCompletion = completion;
            await ApplyPendingKeyboardIfSafeAsync().ConfigureAwait(false);
        }).ConfigureAwait(false);
        if (!accepted)
        {
            try { keyboard.Dispose(); } catch { }
            return;
        }
        await completion.Task.ConfigureAwait(false);
    }

    public Task DrainAsync()
    {
        lock (_commandGate) return _commandTail;
    }

    public Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        lock (_shutdownGate)
        {
            if (_shutdownTask != null) return _shutdownTask;
            Volatile.Write(ref _shuttingDown, 1);
            return _shutdownTask = BeginShutdownAsync(cancellationToken);
        }
    }

    private async Task BeginShutdownAsync(CancellationToken cancellationToken)
    {
        _shutdownCancellation.Cancel();
        try { _session?.Cancellation.Cancel(); } catch (ObjectDisposedException) { }
        using var cancellationRegistration = cancellationToken.Register(_delivery.CancelPending);
        _delivery.StopAccepting();
        await EnqueueCommand(
            () => ShutdownCoreAsync(cancellationToken),
            allowDuringShutdown: true).ConfigureAwait(false);
    }

    private void OnDictationKeyChanged(HotkeyGesture gesture, bool pressed)
    {
        if (Volatile.Read(ref _shuttingDown) != 0 || IsSuspended) return;
        bool suppressAction = IsBusy;
        if (suppressAction && pressed) PlaySound(start: false, _config);
        Observe(EnqueueCommand(() => HandleDictationKeyCoreAsync(pressed, suppressAction)));
    }

    private void OnPinToggled(HotkeyGesture gesture)
    {
        if (Volatile.Read(ref _shuttingDown) == 0 && !IsSuspended) Observe(TogglePinAsync());
    }

    private void OnKeyDiscovered(HotkeyGesture gesture)
    {
        if (IsSuspended) return;
        if (gesture == _lastDiscovered) return;
        _lastDiscovered = gesture;
        string name = gesture.ToString();
        Logger.Info($"Tecla detectada: {name}");
        Notify("Tecla detectada",
            $"Atalho: {name}\nColoque \"hotkey\": \"{name}\" em appsettings.json.");
    }

    private async Task HandleDictationKeyCoreAsync(bool pressed, bool suppressAction = false)
    {
        try
        {
            if (_config.DiscoverMode || IsSuspended) return;
            _models.Touch();

            if (pressed)
            {
                if (_dictationKeyDown) return;
                _dictationKeyDown = true;
            }
            else
            {
                if (!_dictationKeyDown) return;
                _dictationKeyDown = false;
            }

            if (suppressAction) return;

            string mode = _session?.Config.Mode ?? _config.Mode;
            if (mode is "hold" or "push")
            {
                if (pressed && _session == null) await StartSessionAsync().ConfigureAwait(false);
                else if (!pressed && _session != null) await StopSessionAsync().ConfigureAwait(false);
                return;
            }

            if (!pressed) return;
            if (_session == null) await StartSessionAsync().ConfigureAwait(false);
            else await StopSessionAsync().ConfigureAwait(false);
        }
        finally
        {
            await ApplyPendingKeyboardIfSafeAsync().ConfigureAwait(false);
        }
    }

    private async Task ToggleDictationFromUiCoreAsync(TargetToken? deliveryTarget)
    {
        if (_config.Mode != "toggle")
            throw new InvalidOperationException("O botão central grava somente no modo toggle.");
        if (_config.DiscoverMode || IsSuspended)
            throw new InvalidOperationException("O ditado não está disponível neste momento.");
        if (IsBusy) return;

        _models.Touch();
        if (_session == null) await StartSessionAsync(deliveryTarget).ConfigureAwait(false);
        else await StopSessionAsync().ConfigureAwait(false);
    }

    private async Task StartSessionAsync(TargetToken? deliveryTarget = null)
    {
        DictationSession session;
        Config snapshot;
        bool streaming;
        lock (_lifecycleGate)
        {
            if (_lifecycleState != LifecycleActive || _session != null || IsBusy) return;

            snapshot = _config;
            streaming = snapshot.Mode is "live" or "push";
            _models.BeginUse();
            var sessionCancellation = new CancellationTokenSource();
            session = new DictationSession
            {
                Config = snapshot,
                Streaming = streaming,
                Model = _models.GetModelAsync(sessionCancellation.Token),
                PostProcessor = _postProcessor,
                History = _history,
                Cancellation = sessionCancellation,
                DeliveryTarget = deliveryTarget,
            };
            _session = session;
        }

        try
        {
            if (streaming)
            {
                session.Segments = new System.Collections.Concurrent.BlockingCollection<float[]>();
                session.Detector = new VoiceActivityDetector();
                session.Detector.SegmentReady += segment =>
                {
                    try { session.Segments.Add(segment); }
                    catch (InvalidOperationException) { }
                };
                session.Detector.Start(
                    snapshot.EffectiveVadThreshold,
                    snapshot.SilenceMs,
                    snapshot.PhraseMaxSeconds);
                session.Consumer = Task.Run(() => ConsumeSegmentsAsync(session));
                _audio.FrameCaptured += OnAudioFrame;
            }

            int soundMilliseconds = PlaySound(start: true, snapshot);
            await _audio.StartAsync(
                snapshot.InputDevice,
                TimeSpan.FromMilliseconds(MuteWindowMilliseconds(soundMilliseconds)),
                session.Cancellation.Token)
                .ConfigureAwait(false);
            _models.Touch();
            SetRecording(snapshot);
            Logger.Info(streaming
                ? $"Live (VAD) iniciado. silenceMs={snapshot.SilenceMs} threshold={snapshot.EffectiveVadThreshold} phraseMax={snapshot.PhraseMaxSeconds}s"
                : "Gravando...");
        }
        catch (OperationCanceledException) when (session.Cancellation.IsCancellationRequested)
        {
            Logger.Info("Inicio da gravacao cancelado pelo ciclo de energia.");
            await AbortStartAsync(session).ConfigureAwait(false);
            SetIdle();
        }
        catch (Exception exception)
        {
            Logger.Error(streaming ? "Falha ao iniciar modo live" : "Falha ao iniciar gravacao", exception);
            Notify("Erro", "Nao consegui acessar o microfone. Veja matraca.log.", ShellNotificationLevel.Error);
            await AbortStartAsync(session).ConfigureAwait(false);
            SetIdle();
        }
    }

    private void OnAudioFrame(ReadOnlyMemory<float> frame)
    {
        var session = _session;
        if (session?.Streaming != true || session.Detector == null) return;
        try { session.Detector.Feed(frame.Span); }
        catch (Exception exception) { Logger.Error("Falha no detector de voz", exception); }
    }

    private async Task StopSessionAsync()
    {
        var session = _session;
        if (session == null) return;

        Volatile.Write(ref _busy, 1);
        SetBusy(session.Config);
        try
        {
            if (session.Streaming)
                await StopStreamingSessionAsync(session).ConfigureAwait(false);
            else
                await StopBufferedSessionAsync(session).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            if (exception is OperationCanceledException && session.Cancellation.IsCancellationRequested)
            {
                Logger.Info("Ditado ativo cancelado pelo ciclo de vida.");
                return;
            }
            Logger.Error("Falha na transcricao", exception);
            Notify("Erro", "Falha ao transcrever. Veja matraca.log.", ShellNotificationLevel.Error);
        }
        finally
        {
            await FinishSessionAsync(session).ConfigureAwait(false);
            Volatile.Write(ref _busy, 0);
            SetIdle();
        }
    }

    private async Task StopBufferedSessionAsync(DictationSession session)
    {
        float[] samples = await _audio.StopAsync().ConfigureAwait(false);
        PlaySound(start: false, session.Config);
        Logger.Info($"Gravacao parada: {samples.Length} amostras (~{samples.Length / 16000.0:F1}s). Transcrevendo...");

        var model = await session.Model.ConfigureAwait(false);
        if (model == null)
        {
            Notify("Erro", "Modelo nao carregado. Veja matraca.log.", ShellNotificationLevel.Error);
            return;
        }

        string text = await model.TranscribeAsync(samples, session.Cancellation.Token).ConfigureAwait(false);
        _models.Touch();
        Logger.Info($"Transcrito: \"{text}\"");
        if (string.IsNullOrWhiteSpace(text))
        {
            Notify("Vazio", "Nao entendi nenhum audio.");
            return;
        }

        TextReviewResult review = await PostProcessAsync(session, text).ConfigureAwait(false);
        text = review.Text!;
        session.DeliveredSpeech = await DeliverAsync(
            session,
            text,
            session.Config.AutoEnter,
            reviewUsage: review.Usage)
            .ConfigureAwait(false) == TextDeliveryResult.Delivered;
    }

    private async Task StopStreamingSessionAsync(DictationSession session)
    {
        _audio.FrameCaptured -= OnAudioFrame;
        try { await _audio.StopAsync().ConfigureAwait(false); }
        catch (Exception exception) { Logger.Error("Erro ao parar live", exception); }

        try { session.Detector?.Stop(); }
        catch (Exception exception) { Logger.Error("Erro ao finalizar VAD", exception); }
        PlaySound(start: false, session.Config);
        session.Segments?.CompleteAdding();
        if (session.Consumer != null) await session.Consumer.ConfigureAwait(false);

        if (!session.Cancellation.IsCancellationRequested
            && session.DeliveredSpeech
            && !session.DeliveryFailed
            && session.Config.AutoEnter)
        {
            await DeliverAsync(session, "", pressEnter: true, addToHistory: false)
                .ConfigureAwait(false);
            Logger.Info("[live] fim de sessao: Enter final enviado.");
        }
        Logger.Info("Live (VAD) parado.");
    }

    private async Task ConsumeSegmentsAsync(DictationSession session)
    {
        try
        {
            foreach (float[] segment in session.Segments!.GetConsumingEnumerable())
            {
                try
                {
                    var model = await session.Model.ConfigureAwait(false);
                    if (model == null) continue;
                    _models.Touch();
                    string text = await model.TranscribeAsync(segment, session.Cancellation.Token).ConfigureAwait(false);
                    _models.Touch();
                    if (string.IsNullOrWhiteSpace(text)) continue;

                    TextReviewResult review = await PostProcessAsync(session, text).ConfigureAwait(false);
                    text = review.Text!;
                    string chunk = text.Trim() + " ";
                    if (await DeliverAsync(
                            session,
                            chunk,
                            pressEnter: false,
                            reviewUsage: review.Usage).ConfigureAwait(false)
                        == TextDeliveryResult.Delivered)
                        session.DeliveredSpeech = true;
                    else
                        session.DeliveryFailed = true;
                    Logger.Info($"[live] chunk (~{segment.Length / 16000.0:F1}s): \"{text.Trim()}\"");
                }
                catch (OperationCanceledException) when (session.Cancellation.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    session.DeliveryFailed = true;
                    Logger.Error("[live] falha ao transcrever chunk", exception);
                }
            }
        }
        catch (OperationCanceledException) when (session.Cancellation.IsCancellationRequested)
        {
            Logger.Info("Consumidor live cancelado pelo ciclo de vida.");
        }
        catch (Exception exception)
        {
            Logger.Error("[live] consumidor abortou", exception);
        }
    }

    private async Task<TextReviewResult> PostProcessAsync(DictationSession session, string text)
    {
        TextReviewResult result = session.PostProcessor == null
            ? new TextReviewResult(text, null)
            : await session.PostProcessor
                .CleanWithUsageAsync(text, session.Cancellation.Token)
                .ConfigureAwait(false);
        if (result.Usage != null) _usageLedger?.Add(result.Usage);
        return result;
    }

    private async Task<TextDeliveryResult> DeliverAsync(
        DictationSession session,
        string text,
        bool pressEnter,
        bool addToHistory = true,
        TextReviewUsage? reviewUsage = null)
    {
        if (Volatile.Read(ref _shuttingDown) != 0 || session.Cancellation.IsCancellationRequested)
            return TextDeliveryResult.Cancelled;
        if (!session.Streaming)
            SetShellState(ShellState.Busy, "Matraca - escrevendo...");
        if (text.Length > 0) DeliveryStarted?.Invoke(session.Streaming);
        if (addToHistory) session.History?.Add(text, reviewUsage);

        TextDeliveryResult result = await DeliverCoreAsync(session, text, pressEnter)
            .ConfigureAwait(false);
        if (text.Length > 0) DeliveryCompleted?.Invoke(text, result, session.Streaming);
        return result;
    }

    private async Task<TextDeliveryResult> DeliverCoreAsync(
        DictationSession session,
        string text,
        bool pressEnter)
    {
        TargetToken? pinnedTarget = AcquirePinnedTarget();
        if (pinnedTarget != null)
        {
            try
            {
                if (!_targets.IsAlive(pinnedTarget))
                {
                    Unpin(pinnedTarget, "Janela fixada sumiu", "Ela foi fechada; o ditado volta pra janela em foco.");
                }
                else
                {
                    bool noFocus = session.Config.PinDelivery == "nofocus";
                    var result = await EnqueueDeliveryAsync(session, new TextDeliveryRequest(
                        text,
                        pressEnter,
                        noFocus ? TextDeliveryMethod.TargetWithoutFocus : TextDeliveryMethod.TargetWithFocus,
                        pinnedTarget)).ConfigureAwait(false);

                    if (noFocus && result == TextDeliveryResult.Unsupported)
                    {
                        result = await EnqueueDeliveryAsync(session, new TextDeliveryRequest(
                            text,
                            pressEnter,
                            TextDeliveryMethod.TargetWithFocus,
                            pinnedTarget)).ConfigureAwait(false);
                    }

                    if (result != TextDeliveryResult.TargetUnavailable) return result;
                    Unpin(pinnedTarget, "Janela fixada sumiu", "Ela foi fechada; o ditado volta pra janela em foco.");
                }
            }
            finally
            {
                ReleaseTargetUse(pinnedTarget);
            }
        }

        TargetToken? deliveryTarget = session.DeliveryTarget;
        if (deliveryTarget != null)
        {
            if (!_targets.IsAlive(deliveryTarget)) return TextDeliveryResult.TargetUnavailable;
            return await EnqueueDeliveryAsync(session, new TextDeliveryRequest(
                text,
                pressEnter,
                TextDeliveryMethod.TargetWithFocus,
                deliveryTarget)).ConfigureAwait(false);
        }

        var fallbackMethod = session.Config.PasteMethod == "clipboard"
            ? TextDeliveryMethod.Clipboard
            : TextDeliveryMethod.Unicode;
        return await EnqueueDeliveryAsync(session, new TextDeliveryRequest(
            text,
            pressEnter,
            fallbackMethod)).ConfigureAwait(false);
    }

    private Task<TextDeliveryResult> EnqueueDeliveryAsync(
        DictationSession session,
        TextDeliveryRequest request)
    {
        lock (_lifecycleGate)
        {
            if (_lifecycleState != LifecycleActive
                || session.Cancellation.IsCancellationRequested)
                return Task.FromResult(TextDeliveryResult.Cancelled);
            return _delivery.EnqueueAsync(request);
        }
    }

    private Task TogglePinCoreAsync()
    {
        if (_config.DiscoverMode || IsSuspended) return Task.CompletedTask;
        TargetToken? pinnedTarget;
        lock (_targetGate) pinnedTarget = _pinnedTarget;
        if (pinnedTarget != null)
        {
            Unpin(pinnedTarget, "Destino liberado", "O ditado volta pra janela em foco.");
            return Task.CompletedTask;
        }

        var target = _targets.CaptureActive();
        if (target == null)
        {
            Notify("Nada pra fixar", "Nao consegui identificar a janela em foco.", ShellNotificationLevel.Warning);
            return Task.CompletedTask;
        }

        string pinnedTitle;
        try { pinnedTitle = _targets.GetTitle(target); }
        catch (Exception exception)
        {
            ReleaseTarget(target);
            Logger.Error("Falha ao identificar destino fixo", exception);
            Notify("Nada pra fixar", "Nao consegui identificar a janela em foco.", ShellNotificationLevel.Warning);
            return Task.CompletedTask;
        }
        lock (_targetGate)
        {
            _pinnedTarget = target;
            _pinnedTitle = pinnedTitle;
        }
        Logger.Info($"Destino fixado: token={target.Value} \"{pinnedTitle}\"");
        Notify("Destino fixado", $"O ditado vai sempre para: {ShortTitle(pinnedTitle)}\n" +
            $"Aperte {_config.PinHotkeyName} de novo para liberar.");
        RefreshState();
        return Task.CompletedTask;
    }

    private void Unpin(TargetToken target, string title, string message)
    {
        bool release;
        lock (_targetGate)
        {
            if (!ReferenceEquals(_pinnedTarget, target)) return;
            _pinnedTarget = null;
            _pinnedTitle = "";
            release = !_targetUseCounts.ContainsKey(target);
            if (!release) _targetsPendingRelease.Add(target);
        }
        if (release) ReleaseTarget(target);
        Logger.Info("Destino fixo liberado.");
        Notify(title, message);
        RefreshState();
    }

    private TargetToken? AcquirePinnedTarget()
    {
        lock (_targetGate)
        {
            TargetToken? target = _pinnedTarget;
            if (target != null)
                _targetUseCounts[target] = _targetUseCounts.GetValueOrDefault(target) + 1;
            return target;
        }
    }

    private void ReleaseTargetUse(TargetToken target)
    {
        bool release = false;
        lock (_targetGate)
        {
            int remaining = _targetUseCounts[target] - 1;
            if (remaining > 0)
                _targetUseCounts[target] = remaining;
            else
            {
                _targetUseCounts.Remove(target);
                release = _targetsPendingRelease.Remove(target);
            }
        }
        if (release) ReleaseTarget(target);
    }

    private void ReleaseTarget(TargetToken target)
    {
        try { _targets.Release(target); }
        catch (Exception exception) { Logger.Error("Falha ao liberar destino fixo", exception); }
    }

    private async Task ApplyConfigCoreAsync(
        Config config,
        Action<TaskCompletionSource>? deferred = null)
    {
        Config old = _config;
        if (_session != null && ModelChanged(old, config))
        {
            _pendingModelReloadCompletion?.TrySetException(
                new InvalidOperationException("Model reload was superseded."));
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingModelReload = config;
            _pendingModelReloadCompletion = completion;
            deferred?.Invoke(completion);
            Logger.Info("Reload do modelo agendado para o fim da sessao atual.");
            return;
        }
        if (_session != null && _pendingModelReload != null)
        {
            _pendingModelReload = null;
            _pendingModelReloadCompletion?.TrySetException(
                new InvalidOperationException("Model reload was superseded."));
            _pendingModelReloadCompletion = null;
        }

        if (ModelChanged(old, config))
        {
            var model = await _models.ReloadAsync(config, _shutdownCancellation.Token).ConfigureAwait(false);
            if (model == null)
                throw new InvalidOperationException("O novo modelo Whisper nao ficou pronto; configuracao anterior preservada.");
        }

        _config = config;
        _models.SetIdleUnloadMinutes(config.IdleUnloadMinutes);

        if (PostProcessorChanged(old, config))
        {
            var previous = _postProcessor;
            _postProcessor = CreatePostProcessor(config);
            RetirePostProcessor(previous);
        }
        if (old.History != config.History || old.HistoryMaxItems != config.HistoryMaxItems)
        {
            if (_session != null)
                _historyConfigurationPending = true;
            else
                ApplyHistoryConfiguration(config);
        }

        if (_session == null)
        {
            ConfigureIndicator(config);
            SetIdle();
        }
        Logger.Info("Configuracoes aplicadas sem reiniciar.");
    }

    private async Task FinishSessionAsync(DictationSession session)
    {
        if (ReferenceEquals(_session, session)) _session = null;
        session.Segments?.Dispose();
        session.Cancellation.Dispose();
        _models.EndUse();
        DisposeRetiredPostProcessors();

        if (_historyConfigurationPending)
        {
            _historyConfigurationPending = false;
            if (Volatile.Read(ref _shuttingDown) == 0)
                ApplyHistoryConfiguration(_config);
        }

        if (_pendingModelReload != null)
        {
            Config pending = _pendingModelReload;
            TaskCompletionSource? completion = _pendingModelReloadCompletion;
            _pendingModelReload = null;
            _pendingModelReloadCompletion = null;
            if (Volatile.Read(ref _shuttingDown) != 0)
            {
                completion?.TrySetException(new ObjectDisposedException(nameof(DictationController)));
            }
            else try
            {
                await ApplyConfigCoreAsync(pending).ConfigureAwait(false);
                completion?.TrySetResult();
            }
            catch (Exception exception)
            {
                completion?.TrySetException(exception);
                Logger.Error("Falha ao aplicar reload agendado do modelo", exception);
                Notify("Erro", "O novo modelo nao carregou; mantive a configuracao anterior.", ShellNotificationLevel.Error);
            }
        }
        ConfigureIndicator(_config);
    }

    private async Task AbortStartAsync(DictationSession session)
    {
        _audio.FrameCaptured -= OnAudioFrame;
        try { if (_audio.IsCapturing) await _audio.StopAsync().ConfigureAwait(false); } catch { }
        try { session.Detector?.Stop(); } catch { }
        session.Segments?.CompleteAdding();
        try { if (session.Consumer != null) await session.Consumer.ConfigureAwait(false); } catch { }
        await FinishSessionAsync(session).ConfigureAwait(false);
    }

    private void OnModelStateChanged(TranscriptionModelState state)
    {
        if (_session != null || IsBusy || _config.DiscoverMode || IsSuspended) return;
        switch (state)
        {
            case TranscriptionModelState.Loading:
                SetShellState(ShellState.Busy, "Matraca — carregando modelo...");
                break;
            case TranscriptionModelState.Ready:
                SetIdle();
                if (!_announcedReady)
                {
                    _announcedReady = true;
                    Notify("Pronto", $"Atalho: {_config.HotkeyName} · modo: {_config.Mode} · {_config.Gpu}.");
                }
                break;
            case TranscriptionModelState.Failed:
                SetShellState(ShellState.Error, "Matraca — ERRO ao carregar modelo");
                Notify("Erro", "Nao consegui carregar o modelo Whisper. Veja matraca.log.", ShellNotificationLevel.Error);
                break;
            case TranscriptionModelState.Unloaded:
                SetShellState(ShellState.Idle, $"Matraca — ocioso, VRAM liberada ({_config.HotkeyName})");
                break;
        }
    }

    private void RefreshState()
    {
        if (IsSuspended)
        {
            SetSuspended();
            return;
        }
        var session = _session;
        if (session != null) SetRecording(session.Config);
        else if (IsBusy) SetBusy(_config);
        else SetIdle();
    }

    private void SetIdle()
    {
        if (IsSuspended)
        {
            SetSuspended();
            return;
        }
        TargetToken? pinnedTarget;
        string pinnedTitle;
        lock (_targetGate)
        {
            pinnedTarget = _pinnedTarget;
            pinnedTitle = _pinnedTitle;
        }
        string text = _config.DiscoverMode
            ? "Matraca — MODO DESCOBERTA"
            : pinnedTarget != null
                ? $"Matraca — fixado em: {ShortTitle(pinnedTitle)}"
                : $"Matraca — pronto ({_config.HotkeyName})";
        SetShellState(ShellState.Idle, text);
        Dispatch(() =>
        {
            lock (_targetGate)
            {
                if (_pinnedTarget != null)
                    _targets.ShowIndicator(_pinnedTarget, _config.FocusBorderColorPinned);
                else
                    _targets.HideIndicator();
            }
        });
    }

    private void SetSuspended()
    {
        SetShellState(ShellState.Idle, "Matraca - suspenso");
        Dispatch(_targets.HideIndicator);
    }

    private void SetRecording(Config config)
    {
        string instruction = config.HotkeyNeedsKeyUp
            ? "solte para parar"
            : "aperte de novo p/ parar";
        SetShellState(ShellState.Recording, $"Matraca — GRAVANDO ({instruction})");
        Dispatch(() =>
        {
            lock (_targetGate)
            {
                _targets.ShowIndicator(
                    _pinnedTarget,
                    _pinnedTarget != null ? config.FocusBorderColorPinned : config.FocusBorderColor);
            }
        });
    }

    private void SetBusy(Config config)
    {
        SetShellState(ShellState.Busy, "Matraca — transcrevendo...");
        Dispatch(() =>
        {
            lock (_targetGate)
            {
                _targets.ShowIndicator(
                    _pinnedTarget,
                    _pinnedTarget != null ? config.FocusBorderColorPinned : config.FocusBorderColorBusy);
            }
        });
    }

    private void ConfigureIndicator(Config config)
        => Dispatch(() => _targets.ConfigureIndicator(
            config.FocusBorder,
            config.FocusBorderColor,
            config.FocusBorderThickness,
            config.FocusBorderOpacity));

    private int PlaySound(bool start, Config config)
    {
        if (!config.Beep) return 0;
        string file = start ? config.StartSound : config.StopSound;
        return _shell.PlaySound(start, file, config.BeepVolume);
    }

    private int MuteWindowMilliseconds(int soundMilliseconds)
    {
        if (soundMilliseconds <= 0) return 0;
        int window = soundMilliseconds + 150;
        if (window <= MaximumMuteWindowMilliseconds) return window;
        if (!_warnedLongBeep)
        {
            _warnedLongBeep = true;
            Logger.Warn($"Som de inicio longo ({soundMilliseconds}ms): a captura so' descarta {MaximumMuteWindowMilliseconds}ms.");
        }
        return MaximumMuteWindowMilliseconds;
    }

    private TextPostProcessor? CreatePostProcessor(Config config)
    {
        try { return _postProcessorFactory(config); }
        catch (Exception exception)
        {
            Logger.Error("Falha ao iniciar o pos-processamento; seguindo sem ele", exception);
            return null;
        }
    }

    private DictationHistory? CreateHistory(Config config)
    {
        try { return _historyFactory(config); }
        catch (Exception exception)
        {
            Logger.Error("Falha ao iniciar o historico; seguindo sem ele", exception);
            return null;
        }
    }

    private void ApplyHistoryConfiguration(Config config)
    {
        if (!config.History)
        {
            _history = null;
            return;
        }

        if (_history == null)
            _history = CreateHistory(config);
        else
            _history.SetMaximumItems(config.HistoryMaxItems);
    }

    private void RetirePostProcessor(TextPostProcessor? processor)
    {
        if (processor == null || ReferenceEquals(processor, _postProcessor)) return;
        if (_session != null) _retiredPostProcessors.Add(processor);
        else processor.Dispose();
    }

    private void DisposeRetiredPostProcessors()
    {
        foreach (var processor in _retiredPostProcessors)
        {
            try { processor.Dispose(); } catch { }
        }
        _retiredPostProcessors.Clear();
    }

    private void SubscribeKeyboard(IKeyboardHook keyboard)
    {
        keyboard.DictationKeyChanged += OnDictationKeyChanged;
        keyboard.PinToggled += OnPinToggled;
        keyboard.KeyDiscovered += OnKeyDiscovered;
    }

    private void UnsubscribeKeyboard(IKeyboardHook keyboard)
    {
        keyboard.DictationKeyChanged -= OnDictationKeyChanged;
        keyboard.PinToggled -= OnPinToggled;
        keyboard.KeyDiscovered -= OnKeyDiscovered;
    }

    private async Task ApplyPendingKeyboardIfSafeAsync()
    {
        if (_pendingKeyboard == null || _session != null || IsBusy || _dictationKeyDown) return;

        IKeyboardHook keyboard = _pendingKeyboard;
        Config? config = _pendingKeyboardConfig;
        TaskCompletionSource? completion = _pendingKeyboardCompletion;
        _pendingKeyboard = null;
        _pendingKeyboardConfig = null;
        _pendingKeyboardCompletion = null;
        try
        {
            lock (_lifecycleGate)
            {
                UnsubscribeKeyboard(_keyboard);
                try { _keyboard.Dispose(); } catch { }
                _keyboard = keyboard;
                _keyboard.Suspended = _lifecycleState == LifecycleSuspended;
                SubscribeKeyboard(_keyboard);
                _keyboard.Start();
            }
            if (config != null) await ApplyConfigCoreAsync(config).ConfigureAwait(false);
            completion?.TrySetResult();
        }
        catch (Exception exception)
        {
            completion?.TrySetException(exception);
        }
    }

    private Task EnqueueCommand(Func<Task> command, bool allowDuringShutdown = false)
    {
        if (!allowDuringShutdown && Volatile.Read(ref _shuttingDown) != 0)
            return Task.CompletedTask;
        lock (_commandGate)
        {
            _commandTail = RunAfterAsync(_commandTail, command);
            return _commandTail;
        }
    }

    private static async Task RunAfterAsync(Task previous, Func<Task> command)
    {
        try { await previous.ConfigureAwait(false); } catch { }
        await Task.Yield();
        await command().ConfigureAwait(false);
    }

    private async Task SuspendCoreAsync()
    {
        var session = _session;
        if (session != null)
        {
            Volatile.Write(ref _busy, 1);
            try
            {
                session.Cancellation.Cancel();
                _audio.FrameCaptured -= OnAudioFrame;
                try
                {
                    if (_audio.IsCapturing) await _audio.StopAsync().ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    Logger.Error("Falha ao parar audio antes do repouso", exception);
                }
                try { session.Detector?.Stop(); } catch { }
                session.Segments?.CompleteAdding();
                try
                {
                    if (session.Consumer != null) await session.Consumer.ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
                await FinishSessionAsync(session).ConfigureAwait(false);
            }
            finally
            {
                Volatile.Write(ref _busy, 0);
            }
        }

        await ApplyPendingKeyboardIfSafeAsync().ConfigureAwait(false);
        if (IsSuspended) SetSuspended();
        Logger.Info("Pipeline de ditado suspenso para repouso.");
    }

    private Task ResumeCoreAsync()
    {
        lock (_lifecycleGate)
        {
            if (_lifecycleState != LifecycleResuming) return Task.CompletedTask;
            _dictationKeyDown = false;
            _lifecycleState = LifecycleActive;
        }

        SetIdle();
        Logger.Info("Pipeline de ditado retomado apos repouso.");
        return Task.CompletedTask;
    }

    private void Observe(Task task)
        => _ = task.ContinueWith(
            failed => Logger.Error("Falha no pipeline de ditado", failed.Exception!.GetBaseException()),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);

    private void Dispatch(Action action)
    {
        try { _dispatch(action); }
        catch (Exception exception) { Logger.Error("Falha ao atualizar a interface", exception); }
    }

    private void SetShellState(ShellState state, string text)
        => Dispatch(() => _shell.SetState(state, text));

    private void Notify(
        string title,
        string message,
        ShellNotificationLevel level = ShellNotificationLevel.Info)
        => Dispatch(() => _shell.ShowNotification(title, message, level));

    private static string ShortTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return "(janela sem titulo)";
        return title.Length <= 60 ? title : title[..57] + "...";
    }

    private static bool ModelChanged(Config first, Config second)
        => first.ModelPath != second.ModelPath
            || first.Language != second.Language
            || !first.Vocabulary.SequenceEqual(second.Vocabulary, StringComparer.Ordinal);

    private static bool PostProcessorChanged(Config first, Config second)
        => first.PostProcess != second.PostProcess
            || first.PostProcessProvider != second.PostProcessProvider
            || first.PostProcessEndpoint != second.PostProcessEndpoint
            || first.PostProcessModel != second.PostProcessModel
            || first.PostProcessApiKey != second.PostProcessApiKey
            || first.PostProcessPrompt != second.PostProcessPrompt
            || first.PostProcessTimeoutMs != second.PostProcessTimeoutMs;

    private async Task ShutdownCoreAsync(CancellationToken cancellationToken)
    {
        if (_session != null)
        {
            _session.Cancellation.Cancel();
            await StopSessionAsync().ConfigureAwait(false);
        }
        UnsubscribeKeyboard(_keyboard);
        _models.StateChanged -= OnModelStateChanged;
        try
        {
            await _delivery.ShutdownAsync(cancelPending: false, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await _delivery.ShutdownAsync(cancelPending: true).ConfigureAwait(false);
        }
        ReleaseTargetsForShutdown();
        _delivery.Dispose();
        _models.Dispose();
        _shutdownCancellation.Dispose();
        _postProcessor?.Dispose();
        DisposeRetiredPostProcessors();
        if (_pendingKeyboard != null)
        {
            try { _pendingKeyboard.Dispose(); } catch { }
            _pendingKeyboard = null;
            _pendingKeyboardConfig = null;
            _pendingKeyboardCompletion?.TrySetException(
                new ObjectDisposedException(nameof(DictationController)));
            _pendingKeyboardCompletion = null;
        }
        try { _keyboard.Dispose(); } catch { }
        try { _audio.Dispose(); } catch { }
        try { _targets.Dispose(); } catch { }
        try { _shell.Dispose(); } catch { }
        Interlocked.Exchange(ref _disposed, 1);
    }

    private void ReleaseTargetsForShutdown()
    {
        TargetToken[] targets;
        lock (_targetGate)
        {
            if (_pinnedTarget != null) _targetsPendingRelease.Add(_pinnedTarget);
            _pinnedTarget = null;
            _pinnedTitle = "";
            targets = _targetsPendingRelease.ToArray();
            _targetsPendingRelease.Clear();
        }
        foreach (TargetToken target in targets) ReleaseTarget(target);
    }

    public void Dispose()
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        ShutdownAsync().GetAwaiter().GetResult();
    }
}
