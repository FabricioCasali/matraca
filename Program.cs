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
            Application.Run(new TrayApp());
        }
        catch (Exception ex)
        {
            Logger.Error("Falha fatal", ex);
            MessageBox.Show("Erro fatal no Matraca:\n" + ex.Message, "Matraca",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
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

            using var t = new Transcriber(cfg.ModelPath, cfg.Language);
            var live = new LiveDictation();
            int idx = 0;
            live.SegmentReady += seg =>
            {
                int n = ++idx;
                string text = t.TranscribeAsync(seg).GetAwaiter().GetResult();
                Logger.Info($"[LIVE-TESTE] chunk {n} (~{seg.Length / 16000.0:F1}s): \"{text.Trim()}\"");
            };
            Logger.Info($"[LIVE-TESTE] silenceMs={cfg.SilenceMs} threshold={cfg.VadThreshold}");
            live.FeedForTest(samples, cfg.VadThreshold, cfg.SilenceMs);
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

            using var t = new Transcriber(cfg.ModelPath, cfg.Language);
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
    private readonly Config _cfg;
    private readonly NotifyIcon _tray;
    private readonly HotkeyListener _hotkey;
    private readonly AudioRecorder _recorder = new();
    private readonly SynchronizationContext _ui;

    private Transcriber? _transcriber;
    private Task<Transcriber?>? _loadTask;
    private readonly object _gate = new();        // protege _transcriber/_loadTask
    private bool _announcedReady;                  // balão "Pronto" só na 1ª carga
    private bool _busy;          // transcrevendo (modos toggle/hold)
    private int _lastDiscovered; // evita spam de balao no modo descoberta

    // auto-descarregar por inatividade (libera VRAM)
    private long _lastActivityTick;
    private System.Threading.Timer? _idleTimer;

    // moldura visual na janela em foco (mostra onde o texto vai ser colado)
    private readonly FocusBorder? _border;

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

        if (_cfg.FocusBorder)
        {
            try
            {
                var color = ColorTranslator.FromHtml(_cfg.FocusBorderColor);
                _border = new FocusBorder(color, _cfg.FocusBorderThickness, _cfg.FocusBorderOpacity);
            }
            catch (Exception ex)
            {
                Logger.Warn($"Moldura de foco desativada (cor '{_cfg.FocusBorderColor}' invalida? {ex.Message})");
            }
        }

        _hotkey = new HotkeyListener(_cfg);
        _hotkey.Triggered += OnTriggered;
        _hotkey.KeyDiscovered += OnKeyDiscovered;
        _hotkey.Start();

        if (_cfg.DiscoverMode)
        {
            _tray.Text = "Matraca — MODO DESCOBERTA";
            Balloon("Modo descoberta", "Aperte sua tecla custom. O codigo aparece aqui e no matraca.log. " +
                                       "Depois coloque-o em appsettings.json (campo \"hotkey\").");
            Logger.Info("Iniciado em MODO DESCOBERTA de tecla.");
        }

        if (_cfg.IdleUnloadMinutes > 0)
        {
            _idleTimer = new System.Threading.Timer(
                _ => { try { UnloadModelIfIdle(); } catch (Exception ex) { Logger.Error("idle timer", ex); } },
                null, 30_000, 30_000);
            Logger.Info($"Auto-descarregar ocioso: {_cfg.IdleUnloadMinutes} min.");
        }

        _ = EnsureModelLoadedAsync();   // aquece o modelo no startup
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
                    var t = new Transcriber(_cfg.ModelPath, _cfg.Language);
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
            _live.Start(_cfg.VadThreshold, _cfg.SilenceMs);
            Touch();
            SetRecording();
            Beep(true);
            Logger.Info($"Live (VAD) iniciado. silenceMs={_cfg.SilenceMs} threshold={_cfg.VadThreshold}");
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
        Beep(false);
        SetBusy();
        try { _live?.Stop(); } catch (Exception ex) { Logger.Error("Erro ao parar live", ex); }
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
                        string chunk = text.Trim() + " ";
                        _ui.Post(_ => TextInjector.PasteText(chunk, false), null);
                        Logger.Info($"[live] chunk (~{seg.Length / 16000.0:F1}s): \"{text.Trim()}\"");
                    }
                }
                catch (Exception ex) { Logger.Error("[live] falha ao transcrever chunk", ex); }
            }
        }
        catch (Exception ex) { Logger.Error("[live] consumidor abortou", ex); }
    }

    private void StartRecording()
    {
        try
        {
            _recorder.Start();
            Touch();
            SetRecording();
            Beep(true);
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
        Beep(false);
        try
        {
            float[] samples = await _recorder.StopAsync();
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
                _ui.Post(_ => TextInjector.PasteText(text, _cfg.AutoEnter), null);
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

    private void OnKeyDiscovered(int vk)
    {
        if (vk == _lastDiscovered) return;
        _lastDiscovered = vk;
        string name = Config.NameForVk(vk);
        Logger.Info($"Tecla detectada: vk=0x{vk:X2} ({vk}) -> nome sugerido: {name}");
        _ui.Post(_ => Balloon("Tecla detectada",
            $"Codigo: 0x{vk:X2} ({vk})  |  nome: {name}\n" +
            $"Coloque \"hotkey\": \"{name}\" em appsettings.json e reinicie."), null);
    }

    // ---- UI helpers ----
    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Configurações...", null, (_, _) => OpenSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Abrir matraca.log", null, (_, _) =>
        {
            try { System.Diagnostics.Process.Start("notepad.exe", Path.Combine(Logger.DataDir, "matraca.log")); }
            catch { }
        });
        menu.Items.Add("Abrir pasta de config", null, (_, _) =>
        {
            try { System.Diagnostics.Process.Start("explorer.exe", AppContext.BaseDirectory); }
            catch { }
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Sair", null, (_, _) => ExitApp());
        return menu;
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

        var r = MessageBox.Show(
            "Configurações salvas. Reiniciar o Matraca agora para aplicá-las?",
            "Matraca", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (r == DialogResult.Yes) RestartApp();
        else Balloon("Salvo", "As novas configurações valem a partir do próximo início.");
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
            _tray.Visible = false;
        }
        catch { }
        Application.Restart(); // o Main da nova instancia espera o mutex ser liberado
    }

    private void SetIdle()
    {
        _tray.Icon = _icoIdle;
        _tray.Text = _cfg.DiscoverMode
            ? "Matraca — MODO DESCOBERTA"
            : $"Matraca — pronto ({_cfg.HotkeyName})";
        _border?.HideBorder();
    }

    private void SetRecording()
    {
        _tray.Icon = _icoRec;
        _tray.Text = "Matraca — GRAVANDO (aperte de novo p/ parar)";
        _border?.ShowBorder();
    }

    private void SetBusy()
    {
        _tray.Icon = _icoBusy;
        _tray.Text = "Matraca — transcrevendo...";
        // moldura continua visivel: a cola (Ctrl+V) acontece no fim do estado busy,
        // entao a janela marcada ainda e' a que vai receber o texto.
    }

    // Sons distintos: subindo = comecou a gravar; descendo = parou.
    // Se houver um .wav configurado (startSound/stopSound), toca ele; senao, o tom.
    private void Beep(bool start)
    {
        if (!_cfg.Beep) return;
        var file = start ? _cfg.StartSound : _cfg.StopSound;
        if (!string.IsNullOrWhiteSpace(file) && File.Exists(file))
        {
            Beeper.PlaySoundFile(file, _cfg.BeepVolume);
            return;
        }
        var notes = start
            ? new[] { (660, 90), (990, 130) }   // sobe
            : new[] { (990, 90), (590, 150) };   // desce
        Beeper.Play(notes, _cfg.BeepVolume);
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
