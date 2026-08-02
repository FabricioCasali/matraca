using System.Collections.Concurrent;
using System.Media;
using System.Windows.Forms;
using Whisper.net.LibraryLoader;

namespace Matraca;

internal static class Program
{
    /// <summary>
    /// Define qual runtime do Whisper.net usar. Tem efeito ANTES do 1º carregamento do
    /// modelo e é fixado por processo (trocar GPU↔CPU exige reiniciar o app).
    /// </summary>
    internal static void ApplyRuntimePreference(string gpu)
    {
        try
        {
            List<RuntimeLibrary>? order = gpu switch
            {
                "cpu"    => new() { RuntimeLibrary.Cpu, RuntimeLibrary.CpuNoAvx },
                "vulkan" => new() { RuntimeLibrary.Vulkan, RuntimeLibrary.Cpu, RuntimeLibrary.CpuNoAvx },
                _        => null, // "auto": mantém a ordem padrão (GPU se houver, senão CPU)
            };
            if (order != null)
            {
                RuntimeOptions.RuntimeLibraryOrder = order;
                Logger.Info($"Preferência de runtime: {gpu} -> [{string.Join(", ", order)}]");
            }
        }
        catch (Exception ex) { Logger.Error("Falha ao aplicar preferência de runtime", ex); }
    }

    [STAThread]
    private static void Main(string[] args)
    {
        // Modo de teste/diagnostico: transcreve um WAV (16 kHz mono PCM16) e sai.
        // Uso: Matraca.exe --transcribe caminho.wav  -> resultado vai pro matraca.log
        if (args.Length >= 2 && args[0] == "--transcribe")
        {
            RunTranscribeTest(args[1]);
            return;
        }
        if (args.Length >= 2 && args[0] == "--live")
        {
            RunLiveTest(args[1]);
            return;
        }

        // instancia unica; o instalador (Inno AppMutex) usa este nome p/ detectar o app rodando.
        // No restart via tela de config, a nova instancia nasce antes da antiga morrer,
        // entao espera alguns segundos pelo mutex em vez de desistir na hora.
        using var mutex = new Mutex(true, @"Global\MatracaAppMutex", out bool createdNew);
        if (!createdNew)
        {
            try
            {
                if (!mutex.WaitOne(TimeSpan.FromSeconds(8)))
                {
                    Logger.Warn("Matraca ja esta em execucao; esta instancia vai sair.");
                    return;
                }
            }
            catch (AbandonedMutexException) { /* instancia anterior morreu sem liberar; segue */ }
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        try
        {
            RunOnboardingIfNeeded();
            Application.Run(new TrayApp());
        }
        catch (Exception ex)
        {
            Logger.Error("Falha fatal", ex);
            MessageBox.Show("Erro fatal no Matraca:\n" + ex.Message, "Matraca",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Sem um modelo Whisper valido o app nao transcreve nada — antes isso so' aparecia como
    /// um balao de erro depois de iniciar. Agora oferece baixar um na primeira execucao.
    /// Cancelar e' permitido: o app sobe assim mesmo e reclama pelo caminho antigo.
    /// </summary>
    private static void RunOnboardingIfNeeded()
    {
        try
        {
            var modelPath = Environment.ExpandEnvironmentVariables(Config.LoadRaw().modelPath ?? "");
            if (modelPath.Length > 0 && File.Exists(modelPath)) return;

            Logger.Info($"Modelo nao encontrado ('{modelPath}'); abrindo a tela de primeiro uso.");
            using var form = new OnboardingForm();
            form.ShowDialog();
        }
        catch (Exception ex) { Logger.Error("Falha na tela de primeiro uso", ex); }
    }

    private static void RunLiveTest(string wavPath)
    {
        try
        {
            var cfg = Config.Load();
            ApplyRuntimePreference(cfg.Gpu);
            using var reader = new NAudio.Wave.WaveFileReader(wavPath);
            var bytes = new byte[reader.Length];
            int read = reader.Read(bytes, 0, bytes.Length);
            var samples = new float[read / 2];
            for (int i = 0; i < samples.Length; i++)
                samples[i] = BitConverter.ToInt16(bytes, i * 2) / 32768f;

            using var t = new Transcriber(cfg.ModelPath, cfg.Language, cfg.Vocabulary);
            var live = new LiveDictation();
            int idx = 0;
            live.SegmentReady += seg =>
            {
                int n = ++idx;
                string text = t.TranscribeAsync(seg).GetAwaiter().GetResult();
                Logger.Info($"[LIVE-TESTE] chunk {n} (~{seg.Length / 16000.0:F1}s): \"{text.Trim()}\"");
            };
            Logger.Info($"[LIVE-TESTE] silenceMs={cfg.SilenceMs} threshold={cfg.EffectiveVadThreshold} phraseMax={cfg.PhraseMaxSeconds}s");
            live.FeedForTest(samples, cfg.EffectiveVadThreshold, cfg.SilenceMs, cfg.PhraseMaxSeconds);
            Logger.Info($"[LIVE-TESTE] total de chunks: {idx}");
        }
        catch (Exception ex) { Logger.Error("[LIVE-TESTE] Falhou", ex); }
    }

    private static void RunTranscribeTest(string wavPath)
    {
        try
        {
            var cfg = Config.Load();
            ApplyRuntimePreference(cfg.Gpu);
            Whisper.net.Logger.LogProvider.AddLogger((level, msg) =>
                Logger.Info($"[whisper:{level}] {msg?.Trim()}"));
            Logger.Info($"[TESTE] Transcrevendo {wavPath} (gpu={cfg.Gpu})...");
            using var reader = new NAudio.Wave.WaveFileReader(wavPath);
            Logger.Info($"[TESTE] WAV: {reader.WaveFormat}");
            var bytes = new byte[reader.Length];
            int read = reader.Read(bytes, 0, bytes.Length);
            var samples = new float[read / 2];
            for (int i = 0; i < samples.Length; i++)
                samples[i] = BitConverter.ToInt16(bytes, i * 2) / 32768f;

            using var t = new Transcriber(cfg.ModelPath, cfg.Language, cfg.Vocabulary);
            for (int run = 1; run <= 2; run++)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                string text = t.TranscribeAsync(samples).GetAwaiter().GetResult();
                sw.Stop();
                Logger.Info($"[TESTE] run {run}: {sw.ElapsedMilliseconds} ms -> \"{text}\"");
            }
        }
        catch (Exception ex)
        {
            Logger.Error("[TESTE] Falhou", ex);
        }
    }
}

/// <summary>App de bandeja: liga hotkey -> grava -> transcreve -> cola.</summary>
internal sealed class TrayApp : ApplicationContext
{
    // _cfg e os subsistemas que dependem dela nao sao readonly: salvar as configuracoes
    // reconstroi o que precisa em vez de reiniciar o app (ver ApplyConfig).
    private Config _cfg;
    private readonly NotifyIcon _tray;
    private HotkeyListener _hotkey;
    private readonly AudioRecorder _recorder = new();
    private readonly SynchronizationContext _ui;

    private Transcriber? _transcriber;
    private Task<Transcriber?>? _loadTask;
    private readonly object _gate = new();        // protege _transcriber/_loadTask
    private bool _announcedReady;                  // balão "Pronto" só na 1ª carga
    private bool _busy;          // transcrevendo (modos toggle/hold)
    private int _lastDiscovered; // evita spam de balao no modo descoberta
    private KeyMods _lastDiscoveredMods;

    // auto-descarregar por inatividade (libera VRAM)
    private long _lastActivityTick;
    private System.Threading.Timer? _idleTimer;

    // moldura visual na janela em foco (mostra onde o texto vai ser colado)
    private FocusBorder? _border;

    // destino fixo do ditado: enquanto != 0, o texto vai SEMPRE p/ esta janela,
    // independente de qual esta em foco na hora de falar.
    private IntPtr _pinnedHwnd;
    private string _pinnedTitle = "";

    // limpeza opcional do texto por um modelo Claude (null = desligado)
    private TextPostProcessor? _postProcessor;

    // transcricoes recentes guardadas em disco (null = desligado)
    private DictationHistory? _history;

    // modo live (VAD)
    private LiveDictation? _live;
    private BlockingCollection<float[]>? _liveQueue;
    private Task? _liveConsumer;
    private bool _liveActive;

    private readonly Icon _icoIdle = LoadIcon("app.ico", SystemIcons.Application);
    private readonly Icon _icoRec  = LoadIcon("rec.ico", SystemIcons.Exclamation);
    private readonly Icon _icoBusy = LoadIcon("busy.ico", SystemIcons.Information);

    private static Icon LoadIcon(string file, Icon fallback)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, file);
            if (File.Exists(path)) return new Icon(path);
        }
        catch (Exception ex) { Logger.Warn($"Falha ao carregar icone {file}: {ex.Message}"); }
        return fallback;
    }

    public TrayApp()
    {
        _cfg = Config.Load();
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        Program.ApplyRuntimePreference(_cfg.Gpu);
        Touch();

        _tray = new NotifyIcon
        {
            Icon = _icoIdle,
            Visible = true,
            Text = "Matraca — iniciando...",
            ContextMenuStrip = BuildMenu(),
        };

        _border = BuildBorder();
        _postProcessor = TextPostProcessor.TryCreate(_cfg);
        _history = _cfg.History ? new DictationHistory(_cfg.HistoryMaxItems) : null;
        _hotkey = BuildHotkey();

        if (_cfg.DiscoverMode)
        {
            _tray.Text = "Matraca — MODO DESCOBERTA";
            Balloon("Modo descoberta", "Aperte sua tecla custom. O codigo aparece aqui e no matraca.log. " +
                                       "Depois coloque-o em appsettings.json (campo \"hotkey\").");
            Logger.Info("Iniciado em MODO DESCOBERTA de tecla.");
        }

        RebuildIdleTimer();

        _ = EnsureModelLoadedAsync();   // aquece o modelo no startup
    }

    // ---- construcao dos subsistemas que dependem da config (reusado por ApplyConfig) ----

    private FocusBorder? BuildBorder()
    {
        if (!_cfg.FocusBorder) return null;
        try
        {
            var color = ColorTranslator.FromHtml(_cfg.FocusBorderColor);
            return new FocusBorder(color, _cfg.FocusBorderThickness, _cfg.FocusBorderOpacity);
        }
        catch (Exception ex)
        {
            Logger.Warn($"Moldura de foco desativada (cor '{_cfg.FocusBorderColor}' invalida? {ex.Message})");
            return null;
        }
    }

    private HotkeyListener BuildHotkey()
    {
        var h = new HotkeyListener(_cfg);
        h.Triggered += OnTriggered;
        h.KeyDiscovered += OnKeyDiscovered;
        h.PinToggled += OnPinToggled;
        h.Start();
        if (_cfg.PinHotkeyVk != 0)
            Logger.Info($"Fixar janela de destino: tecla {_cfg.PinHotkeyName}.");
        return h;
    }

    private void RebuildIdleTimer()
    {
        _idleTimer?.Dispose();
        _idleTimer = null;
        if (_cfg.IdleUnloadMinutes <= 0) return;
        _idleTimer = new System.Threading.Timer(
            _ => { try { UnloadModelIfIdle(); } catch (Exception ex) { Logger.Error("idle timer", ex); } },
            null, 30_000, 30_000);
        Logger.Info($"Auto-descarregar ocioso: {_cfg.IdleUnloadMinutes} min.");
    }

    private void Touch() => Interlocked.Exchange(ref _lastActivityTick, Environment.TickCount64);

    /// <summary>Garante o modelo carregado (recarrega se foi descarregado). Não bloqueia se já estiver carregando.</summary>
    private Task<Transcriber?> EnsureModelLoadedAsync()
    {
        lock (_gate)
        {
            if (_transcriber != null) return Task.FromResult<Transcriber?>(_transcriber);
            if (_loadTask != null) return _loadTask;

            _ui.Post(_ =>
            {
                if (!_cfg.DiscoverMode && !_recorder.IsRecording && !_liveActive && !_busy)
                {
                    _tray.Icon = _icoBusy;
                    _tray.Text = "Matraca — carregando modelo...";
                }
            }, null);

            _loadTask = Task.Run<Transcriber?>(() =>
            {
                try
                {
                    var t = new Transcriber(_cfg.ModelPath, _cfg.Language, _cfg.Vocabulary);
                    lock (_gate) { _transcriber = t; }
                    _ui.Post(_ =>
                    {
                        if (!_recorder.IsRecording && !_liveActive && !_busy) SetIdle();
                        if (!_announcedReady && !_cfg.DiscoverMode)
                        {
                            _announcedReady = true;
                            Balloon("Pronto", $"Atalho: {_cfg.HotkeyName} · modo: {_cfg.Mode} · {_cfg.Gpu}.");
                        }
                        Logger.Info("Modelo carregado / pronto.");
                    }, null);
                    return t;
                }
                catch (Exception ex)
                {
                    Logger.Error("Falha ao carregar o modelo", ex);
                    _ui.Post(_ =>
                    {
                        _tray.Text = "Matraca — ERRO ao carregar modelo";
                        Balloon("Erro", "Nao consegui carregar o modelo Whisper. Veja matraca.log.",
                            ToolTipIcon.Error);
                    }, null);
                    return null;
                }
                finally { lock (_gate) { _loadTask = null; } }
            });
            return _loadTask;
        }
    }

    /// <summary>Chamado pelo timer: se ocioso há tempo suficiente e nada em uso, libera a VRAM.</summary>
    private void UnloadModelIfIdle()
    {
        if (_cfg.IdleUnloadMinutes <= 0) return;
        long idleMs = Environment.TickCount64 - Interlocked.Read(ref _lastActivityTick);
        if (idleMs < (long)_cfg.IdleUnloadMinutes * 60_000L) return;

        Transcriber? toDispose;
        lock (_gate)
        {
            if (_transcriber == null || _loadTask != null) return;     // nada carregado / carregando
            if (_busy || _liveActive || _recorder.IsRecording) return;  // em uso
            toDispose = _transcriber;
            _transcriber = null;
        }
        try { toDispose.Dispose(); } catch { }
        Logger.Info($"Modelo descarregado por inatividade ({_cfg.IdleUnloadMinutes} min). VRAM liberada.");
        _ui.Post(_ =>
        {
            if (!_cfg.DiscoverMode && !_recorder.IsRecording && !_liveActive && !_busy)
                _tray.Text = $"Matraca — ocioso, VRAM liberada ({_cfg.HotkeyName})";
        }, null);
    }

    // O hook chama isto na propria thread do hook (UI thread). NAO pode bloquear: se o
    // callback do hook demora > LowLevelHooksTimeout (~300ms) o Windows remove o hook
    // silenciosamente e o atalho "morre". Entao apenas re-posta p/ a fila de mensagens e
    // retorna na hora — OnTriggeredCore roda depois, ainda na UI thread (Set* sao seguros).
    private void OnTriggered(bool pressed)
        => _ui.Post(_ => OnTriggeredCore(pressed), null);

    private void OnTriggeredCore(bool pressed)
    {
        if (_cfg.DiscoverMode) return;
        Touch();
        _ = EnsureModelLoadedAsync();   // recarrega se foi descarregado (enquanto você fala)

        if (_cfg.Mode == "live" || _cfg.Mode == "push")
        {
            if (_cfg.Mode == "push")
            {
                // push-to-talk: streaming VAD enquanto a tecla esta pressionada.
                if (pressed) { if (!_liveActive && _liveConsumer == null) StartLive(); }
                else if (_liveActive) { _ = StopLive(); }
            }
            else // live (toggle): aperta p/ comecar, aperta de novo p/ parar
            {
                if (!pressed) return;
                if (_liveActive) { _ = StopLive(); }
                else if (_liveConsumer == null) StartLive();
            }
            return;
        }

        if (_busy) { Beep(false); return; }

        // toggle: cada 'pressed==true' alterna. hold: pressed true=start, false=stop.
        bool start = _cfg.Mode == "hold" ? pressed : !_recorder.IsRecording;
        bool stop  = _cfg.Mode == "hold" ? !pressed : _recorder.IsRecording && !start;

        if (start && !_recorder.IsRecording) StartRecording();
        else if (stop && _recorder.IsRecording) _ = StopAndTranscribe();
    }

    // ---- Modo live (VAD por pausa) ----
    private void StartLive()
    {
        try
        {
            _liveActive = true;
            _liveQueue = new BlockingCollection<float[]>();
            _live = new LiveDictation();
            _live.SegmentReady += seg => { try { _liveQueue?.Add(seg); } catch { } };
            _liveConsumer = Task.Run(ConsumeLiveSegments);
            // idem StartRecording: toca o bip e descarta a janela em que ele soa
            float threshold = _cfg.EffectiveVadThreshold;   // sensibilidade do microfone em uso
            _live.Start(threshold, _cfg.SilenceMs, _cfg.PhraseMaxSeconds,
                        MuteWindowMs(Beep(true)), AudioDevices.Resolve(_cfg.InputDevice));
            Touch();
            SetRecording();
            Logger.Info($"Live (VAD) iniciado. silenceMs={_cfg.SilenceMs} threshold={threshold} phraseMax={_cfg.PhraseMaxSeconds}s");
        }
        catch (Exception ex)
        {
            Logger.Error("Falha ao iniciar modo live", ex);
            Balloon("Erro", "Nao consegui iniciar o modo live. Veja matraca.log.", ToolTipIcon.Error);
            // limpa TODO o estado: senao _liveConsumer fica != null e o atalho cai no
            // "caminho morto" (nem inicia nem para) ate reiniciar o app.
            _liveActive = false;
            try { _live?.Stop(); } catch { }
            _live?.Dispose(); _live = null;
            _liveQueue?.CompleteAdding();
            _liveQueue?.Dispose(); _liveQueue = null;
            _liveConsumer = null;
            SetIdle();
        }
    }

    private async Task StopLive()
    {
        if (!_liveActive) return;
        _liveActive = false;
        Touch();
        SetBusy();
        // para a captura ANTES do bip de fim, senao ele vira um segmento transcrito
        try { _live?.Stop(); } catch (Exception ex) { Logger.Error("Erro ao parar live", ex); }
        Beep(false);
        _liveQueue?.CompleteAdding();                 // sinaliza fim ao consumidor
        try { if (_liveConsumer != null) await _liveConsumer; } catch { }

        _live?.Dispose(); _live = null;
        _liveQueue?.Dispose(); _liveQueue = null;
        _liveConsumer = null;
        SetIdle();
        Logger.Info("Live (VAD) parado.");
    }

    // roda numa thread de fundo; transcreve segmentos em ordem e cola
    private void ConsumeLiveSegments()
    {
        try
        {
            foreach (var seg in _liveQueue!.GetConsumingEnumerable())
            {
                try
                {
                    var transcriber = EnsureModelLoadedAsync().GetAwaiter().GetResult();
                    if (transcriber == null) continue;
                    Touch();
                    string text = transcriber.TranscribeAsync(seg).GetAwaiter().GetResult();
                    Touch();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        // No modo live isto custa uma ida a' rede por frase — o usuario opta
                        // por isso ao ligar o pos-processamento (ver README).
                        if (_postProcessor != null)
                        {
                            text = _postProcessor.CleanAsync(text).GetAwaiter().GetResult();
                            Touch();
                        }
                        string chunk = text.Trim() + " ";
                        _ui.Post(_ => Deliver(chunk, false), null);
                        Logger.Info($"[live] chunk (~{seg.Length / 16000.0:F1}s): \"{text.Trim()}\"");
                    }
                }
                catch (Exception ex) { Logger.Error("[live] falha ao transcrever chunk", ex); }
            }

            // Um Enter por pedaco mandaria cada frase solta; o que encerra a fala e' o fim da sessao.
            if (_cfg.AutoEnter)
            {
                _ui.Post(_ => Deliver("", true), null);
                Logger.Info("[live] fim de sessao: Enter final enviado.");
            }
        }
        catch (Exception ex) { Logger.Error("[live] consumidor abortou", ex); }
    }

    private void StartRecording()
    {
        try
        {
            // O bip sai pelo alto-falante e volta pelo microfone: sem descartar esse trecho,
            // o Whisper transcreve o proprio som como palavra. Toca primeiro (assincrono) e
            // arma a captura ignorando a janela em que o bip ainda esta soando.
            _recorder.Start(MuteWindowMs(Beep(true)), AudioDevices.Resolve(_cfg.InputDevice));
            Touch();
            SetRecording();
            Logger.Info("Gravando...");
        }
        catch (Exception ex)
        {
            Logger.Error("Falha ao iniciar gravacao", ex);
            Balloon("Erro", "Nao consegui acessar o microfone. Veja matraca.log.", ToolTipIcon.Error);
            SetIdle();
        }
    }

    private async Task StopAndTranscribe()
    {
        _busy = true;
        SetBusy();
        try
        {
            // para a captura ANTES do bip de fim, senao ele entra no audio transcrito
            float[] samples = await _recorder.StopAsync();
            Beep(false);
            Logger.Info($"Gravacao parada: {samples.Length} amostras (~{samples.Length / 16000.0:F1}s). Transcrevendo...");

            var transcriber = await EnsureModelLoadedAsync();
            if (transcriber == null)
            {
                Balloon("Erro", "Modelo nao carregado. Veja matraca.log.", ToolTipIcon.Error);
                return;
            }
            string text = await Task.Run(() => transcriber.TranscribeAsync(samples));
            Touch();
            Logger.Info($"Transcrito: \"{text}\"");

            if (string.IsNullOrWhiteSpace(text))
            {
                Balloon("Vazio", "Nao entendi nenhum audio.", ToolTipIcon.Info);
            }
            else
            {
                if (_postProcessor != null) text = await _postProcessor.CleanAsync(text);
                Touch();
                _ui.Post(_ => Deliver(text, _cfg.AutoEnter), null);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Falha na transcricao", ex);
            Balloon("Erro", "Falha ao transcrever. Veja matraca.log.", ToolTipIcon.Error);
        }
        finally
        {
            _busy = false;
            SetIdle();
        }
    }

    // ---- destino fixo (pin de janela) ----
    // O hook chama na thread dele; re-posta p/ a UI thread pelo mesmo motivo do OnTriggered.
    private void OnPinToggled() => _ui.Post(_ => OnPinToggledCore(), null);

    private void OnPinToggledCore()
    {
        if (_cfg.DiscoverMode) return;

        if (_pinnedHwnd != IntPtr.Zero) { Unpin("Destino liberado", "O ditado volta pra janela em foco."); return; }

        var hwnd = TextInjector.GetForegroundWindowHandle();
        if (hwnd == IntPtr.Zero)
        {
            Balloon("Nada pra fixar", "Nao consegui identificar a janela em foco.", ToolTipIcon.Warning);
            return;
        }

        _pinnedHwnd = hwnd;
        _pinnedTitle = TextInjector.GetWindowTitle(hwnd);
        _border?.SetPinned(hwnd);
        Logger.Info($"Destino fixado: hwnd=0x{hwnd.ToInt64():X} \"{_pinnedTitle}\"");
        Balloon("Destino fixado", $"O ditado vai sempre para: {ShortTitle(_pinnedTitle)}\n" +
                                  $"Aperte {_cfg.PinHotkeyName} de novo para liberar.");
        RefreshTrayState();
    }

    private void Unpin(string balloonTitle, string balloonText)
    {
        _pinnedHwnd = IntPtr.Zero;
        _pinnedTitle = "";
        _border?.SetPinned(IntPtr.Zero);
        Logger.Info("Destino fixo liberado.");
        Balloon(balloonTitle, balloonText);
        RefreshTrayState();
    }

    /// <summary>
    /// Reflete a mudanca de destino no tray sem atropelar uma gravacao em andamento —
    /// SetIdle apagaria o icone de gravando e esconderia a moldura no meio do ditado.
    /// </summary>
    private void RefreshTrayState()
    {
        if (_recorder.IsRecording || _liveActive) SetRecording();
        else if (_busy) SetBusy();
        else SetIdle();
    }

    private static string ShortTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return "(janela sem titulo)";
        return title.Length <= 60 ? title : title[..57] + "...";
    }

    /// <summary>
    /// Entrega o texto: na janela fixada, se houver uma valida; senao na janela em foco.
    /// </summary>
    private void Deliver(string text, bool autoEnter)
    {
        _history?.Add(text);

        if (_pinnedHwnd != IntPtr.Zero)
        {
            if (!TextInjector.IsWindowAlive(_pinnedHwnd))
            {
                Unpin("Janela fixada sumiu", "Ela foi fechada; o ditado volta pra janela em foco.");
                // segue adiante e entrega na janela em foco, p/ nao perder a transcricao
            }
            else if (_cfg.PinDelivery == "nofocus")
            {
                TextInjector.SendToWindow(_pinnedHwnd, text, autoEnter);
                return;
            }
            else
            {
                // assincrono: se nao conseguir entregar na janela fixada, cai na janela atual
                // em vez de perder o ditado
                TextInjector.DeliverWithFocus(_pinnedHwnd, text, autoEnter, ok =>
                {
                    if (!ok) _ui.Post(_ => TextInjector.PasteText(text, autoEnter, _cfg.PasteMethod), null);
                });
                return;
            }
        }
        TextInjector.PasteText(text, autoEnter, _cfg.PasteMethod);
    }

    private void OnKeyDiscovered(int vk, KeyMods mods)
    {
        if (vk == _lastDiscovered && mods == _lastDiscoveredMods) return;
        _lastDiscovered = vk;
        _lastDiscoveredMods = mods;
        string name = Config.FormatHotkey(vk, mods);
        Logger.Info($"Tecla detectada: vk=0x{vk:X2} ({vk}) mods={mods} -> atalho sugerido: {name}");
        _ui.Post(_ => Balloon("Tecla detectada",
            $"Codigo: 0x{vk:X2} ({vk})  |  atalho: {name}\n" +
            $"Coloque \"hotkey\": \"{name}\" em appsettings.json e reinicie."), null);
    }

    // ---- UI helpers ----
    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Configurações...", null, (_, _) => OpenSettings());
        if (_history != null)
            menu.Items.Add("Histórico de ditados...", null, (_, _) => OpenHistory());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Abrir matraca.log", null, (_, _) =>
        {
            try { System.Diagnostics.Process.Start("notepad.exe", Path.Combine(Logger.DataDir, "matraca.log")); }
            catch { }
        });
        menu.Items.Add("Abrir pasta de config", null, (_, _) =>
        {
            // a config efetiva mora em %LOCALAPPDATA%\Matraca; a copia ao lado do exe e' so
            // fallback de primeira execucao (e sob uiAccess nem e' gravavel).
            try
            {
                Directory.CreateDirectory(Logger.DataDir);
                System.Diagnostics.Process.Start("explorer.exe", Logger.DataDir);
            }
            catch (Exception ex) { Logger.Warn("Falha ao abrir a pasta de config: " + ex.Message); }
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Sair", null, (_, _) => ExitApp());
        return menu;
    }

    private void OpenHistory()
    {
        if (_history == null) return;
        // captura o alvo ANTES de abrir a tela: a partir daqui o foco e' dela
        var returnTo = TextInjector.GetForegroundWindowHandle();
        using var form = new HistoryForm(_history, _cfg, returnTo);
        form.ShowDialog();
    }

    private void OpenSettings()
    {
        // nao abrir com gravacao/sessao ativa: a config e' aplicada por reinicio
        if (_recorder.IsRecording || _liveActive || _busy)
        {
            Balloon("Aguarde", "Encerre a gravação antes de abrir as configurações.");
            return;
        }
        using var form = new SettingsForm(_hotkey);
        if (form.ShowDialog() != DialogResult.OK) return;
        ApplyConfig();
    }

    /// <summary>
    /// Aplica a config recem-salva sem reiniciar: troca o objeto _cfg e reconstroi so' os
    /// subsistemas cujas opcoes mudaram. Tudo o que e' lido na hora do uso (modo, sons, pausas,
    /// sensibilidade, entrega) passa a valer sozinho.
    ///
    /// Unica excecao: a escolha GPU/CPU e' fixada no processo antes do 1o carregamento do
    /// runtime do Whisper — essa ainda exige reinicio, e so' ela.
    /// </summary>
    private void ApplyConfig()
    {
        var old = _cfg;
        try { _cfg = Config.Load(); }
        catch (Exception ex)
        {
            Logger.Error("Falha ao recarregar a config", ex);
            Balloon("Erro", "Não consegui recarregar as configurações. Veja matraca.log.", ToolTipIcon.Error);
            return;
        }

        if (_cfg.HotkeyVk != old.HotkeyVk || _cfg.HotkeyMods != old.HotkeyMods ||
            _cfg.PinHotkeyVk != old.PinHotkeyVk || _cfg.PinHotkeyMods != old.PinHotkeyMods ||
            _cfg.DiscoverMode != old.DiscoverMode)
        {
            try { _hotkey.Dispose(); } catch { }
            _hotkey = BuildHotkey();
        }

        if (_cfg.FocusBorder != old.FocusBorder ||
            _cfg.FocusBorderColor != old.FocusBorderColor ||
            _cfg.FocusBorderThickness != old.FocusBorderThickness ||
            Math.Abs(_cfg.FocusBorderOpacity - old.FocusBorderOpacity) > 0.001f)
        {
            try { _border?.Dispose(); } catch { }
            _border = BuildBorder();
            // a moldura nova nasce sem saber da janela fixada
            if (_pinnedHwnd != IntPtr.Zero) _border?.SetPinned(_pinnedHwnd);
        }

        if (_cfg.History != old.History || _cfg.HistoryMaxItems != old.HistoryMaxItems)
        {
            _history = _cfg.History ? new DictationHistory(_cfg.HistoryMaxItems) : null;
            // o item "Histórico de ditados..." so' existe quando o recurso esta' ligado
            var oldMenu = _tray.ContextMenuStrip;
            _tray.ContextMenuStrip = BuildMenu();
            try { oldMenu?.Dispose(); } catch { }
        }

        if (_cfg.PostProcess != old.PostProcess ||
            _cfg.PostProcessModel != old.PostProcessModel ||
            _cfg.PostProcessApiKey != old.PostProcessApiKey ||
            _cfg.PostProcessPrompt != old.PostProcessPrompt ||
            _cfg.PostProcessTimeoutMs != old.PostProcessTimeoutMs)
        {
            try { _postProcessor?.Dispose(); } catch { }
            _postProcessor = TextPostProcessor.TryCreate(_cfg);
        }

        if (_cfg.IdleUnloadMinutes != old.IdleUnloadMinutes) RebuildIdleTimer();

        // o modelo carregado carrega idioma e vocabulario dentro dele: recarrega em background
        bool modelChanged = _cfg.ModelPath != old.ModelPath
                         || _cfg.Language != old.Language
                         || !_cfg.Vocabulary.SequenceEqual(old.Vocabulary, StringComparer.Ordinal);
        if (modelChanged) ReloadTranscriber();

        RefreshTrayState();
        Logger.Info("Configuracoes aplicadas sem reiniciar.");

        if (_cfg.Gpu != old.Gpu)
        {
            // RuntimeLibraryOrder so' tem efeito antes do 1o load do runtime nativo, que ja'
            // aconteceu neste processo — nao da' pra trocar GPU/CPU a quente.
            var r = MessageBox.Show(
                "A troca entre GPU e CPU só vale reiniciando o Matraca.\n\n"
              + "Todo o resto já foi aplicado. Reiniciar agora?",
                "Matraca", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r == DialogResult.Yes) { RestartApp(); return; }
        }

        Balloon("Configurações aplicadas", $"Atalho: {_cfg.HotkeyName} · modo: {_cfg.Mode}.");
    }

    /// <summary>Descarta o modelo em memoria e recarrega com o idioma/vocabulario novos.</summary>
    private void ReloadTranscriber()
    {
        Transcriber? toDispose;
        lock (_gate)
        {
            toDispose = _transcriber;
            _transcriber = null;
        }
        try { toDispose?.Dispose(); } catch { }
        _announcedReady = true;   // ja' anunciamos "pronto" uma vez; nao repete o balao
        _ = EnsureModelLoadedAsync();
    }

    private void RestartApp()
    {
        Logger.Info("Reiniciando para aplicar configuracoes...");
        try
        {
            _hotkey.Dispose();
            _idleTimer?.Dispose();
            try { _live?.Stop(); _liveQueue?.CompleteAdding(); } catch { }
            _recorder.Dispose();
            _transcriber?.Dispose();
            _border?.Dispose();
            _postProcessor?.Dispose();
            _tray.Visible = false;
        }
        catch { }
        Application.Restart(); // o Main da nova instancia espera o mutex ser liberado
    }

    private void SetIdle()
    {
        _tray.Icon = _icoIdle;
        _tray.Text = Truncate(_cfg.DiscoverMode
            ? "Matraca — MODO DESCOBERTA"
            : _pinnedHwnd != IntPtr.Zero
                ? $"Matraca — fixado em: {ShortTitle(_pinnedTitle)}"
                : $"Matraca — pronto ({_cfg.HotkeyName})");

        // Com destino fixo, a moldura FICA na janela fixada mesmo fora da gravacao. Fixar e' um
        // modo persistente e facil de esquecer — sem marca permanente voce dita achando que vai
        // pra janela em foco e o texto cai em outro lugar. Serve tambem como a confirmacao que o
        // balao do tray nao garante: o Windows engole balao repetido, a moldura nao depende dele.
        if (_pinnedHwnd != IntPtr.Zero)
        {
            _border?.SetColor(BorderColorFor(recording: false));
            _border?.ShowBorder();
        }
        else _border?.HideBorder();
    }

    // NotifyIcon.Text estoura com mais de 63 caracteres.
    private static string Truncate(string s) => s.Length <= 63 ? s : s[..60] + "...";

    private void SetRecording()
    {
        _tray.Icon = _icoRec;
        _tray.Text = "Matraca — GRAVANDO (aperte de novo p/ parar)";
        _border?.SetColor(BorderColorFor(recording: true));
        _border?.ShowBorder();
    }

    private void SetBusy()
    {
        _tray.Icon = _icoBusy;
        _tray.Text = "Matraca — transcrevendo...";
        // moldura continua visivel: a entrega do texto acontece no fim do estado busy,
        // entao a janela marcada ainda e' a que vai receber o texto. So' a cor muda.
        _border?.SetColor(BorderColorFor(recording: false));
    }

    /// <summary>
    /// Cor da moldura por estado: destino fixo tem prioridade (o texto vai pra la' de todo
    /// jeito), senao vermelho gravando / âmbar transcrevendo.
    /// </summary>
    private Color BorderColorFor(bool recording)
    {
        var html = _pinnedHwnd != IntPtr.Zero
            ? _cfg.FocusBorderColorPinned
            : recording ? _cfg.FocusBorderColor : _cfg.FocusBorderColorBusy;
        try { return ColorTranslator.FromHtml(html); }
        catch { return Color.FromArgb(0xE8, 0x11, 0x23); }
    }

    /// <summary>
    /// Teto da janela de descarte. Cada ms aqui e' um ms de fala que o usuario perde, entao um
    /// som de inicio longo nao pode mandar sozinho: passando disto, o resto dele pode acabar
    /// entrando no audio, e o certo e' escolher um som mais curto.
    /// </summary>
    private const int MaxMuteWindowMs = 900;

    private static bool _warnedLongBeep;

    /// <summary>
    /// Piso da janela de descarte no inicio da captura, pela duracao do som (ja' sem o silencio
    /// do fim). A captura estende isso sozinha enquanto o bip estiver soando de verdade (ver
    /// Beeper.InBeepShadow), que e' o que cobre a latencia de decodificacao e do buffer da placa.
    /// </summary>
    private static int MuteWindowMs(int beepMs)
    {
        if (beepMs <= 0) return 0;
        int window = beepMs + Beeper.GuardMs;
        if (window <= MaxMuteWindowMs) return window;

        if (!_warnedLongBeep)
        {
            _warnedLongBeep = true;
            Logger.Warn($"Som de inicio longo ({beepMs}ms): a captura so' descarta {MaxMuteWindowMs}ms, " +
                        "senao voce perderia esse tanto de fala. Um som mais curto evita o problema.");
        }
        return MaxMuteWindowMs;
    }

    // Sons distintos: subindo = comecou a gravar; descendo = parou.
    // Se houver um .wav configurado (startSound/stopSound), toca ele; senao, o tom.
    // Devolve a duracao do som em ms (0 se nao tocou nada).
    private int Beep(bool start)
    {
        if (!_cfg.Beep) return 0;
        var file = start ? _cfg.StartSound : _cfg.StopSound;
        if (!string.IsNullOrWhiteSpace(file) && File.Exists(file))
            return Beeper.PlaySoundFile(file, _cfg.BeepVolume);

        var notes = start
            ? new[] { (660, 90), (990, 130) }   // sobe
            : new[] { (990, 90), (590, 150) };   // desce
        return Beeper.Play(notes, _cfg.BeepVolume);
    }

    private void Balloon(string title, string text, ToolTipIcon icon = ToolTipIcon.Info)
    {
        _tray.BalloonTipTitle = title;
        _tray.BalloonTipText = text;
        _tray.BalloonTipIcon = icon;
        _tray.ShowBalloonTip(5000);
    }

    private void ExitApp()
    {
        try
        {
            _hotkey.Dispose();
            _idleTimer?.Dispose();
            _border?.Dispose();
            _postProcessor?.Dispose();
            try { _live?.Stop(); _liveQueue?.CompleteAdding(); } catch { }
            _recorder.Dispose();
            _transcriber?.Dispose();
            _tray.Visible = false;
            _tray.Dispose();
            _icoIdle.Dispose(); _icoRec.Dispose(); _icoBusy.Dispose();
        }
        catch { }
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { try { _tray.Dispose(); } catch { } }
        base.Dispose(disposing);
    }
}
