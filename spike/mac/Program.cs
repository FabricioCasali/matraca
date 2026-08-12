using Matraca.MacSpike.Audio;
using Matraca.MacSpike.Hud;
using Matraca.MacSpike.Interop;
using Matraca.MacSpike.Speech;
using Matraca.MacSpike.Tap;
using Matraca.MacSpike.Text;

namespace Matraca.MacSpike;

/// <summary>
/// Ponto de entrada do spike. Uma perna por argumento — e isso nao e' conveniencia de linha
/// de comando: e' o que faz a falha de uma prova nao levar junto as outras quatro.
///
/// A ordem em que as pernas foram escritas (casco assinado -> tap -> audio -> whisper ->
/// injecao -> HUD -> pipeline) tambem nao e' arbitraria: ver o README do spike.
/// </summary>
internal static class Program
{
    /// <summary>Duracao fixa da gravacao. Sem VAD, sem modos — isso e' Fase 2.</summary>
    private const double RecordSeconds = 3.0;

    private static int Main(string[] args)
    {
        string leg = args.Length > 0 ? args[0] : "--alive";
        string? arg = args.Length > 1 ? args[1] : null;

        SpikeLog.Banner(leg);
        SpikeLog.Info($"vivo — pid {Environment.ProcessId}, {System.Runtime.InteropServices.RuntimeInformation.OSDescription}, "
                    + $"{System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");
        SpikeLog.Info($"executavel: {Environment.ProcessPath}");
        SpikeLog.Info($"base dir  : {AppContext.BaseDirectory}");

        try
        {
            return leg switch
            {
                "--alive" => Alive(),
                "--tap" => RunTap(),
                "--audio" => RunAudio(),
                "--whisper" => RunWhisper(arg),
                "--inject" => RunInject(),
                "--hud" => RunHud(),
                "--pipeline" => RunPipeline(),
                _ => Uso(leg),
            };
        }
        catch (Exception ex)
        {
            SpikeLog.Error("perna estourou", ex);
            return 1;
        }
    }

    private static int Uso(string leg)
    {
        SpikeLog.Error($"perna desconhecida: {leg}");
        SpikeLog.Info("uso: Matraca --alive | --tap | --audio | --whisper [arquivo.wav] | "
                    + "--inject | --hud | --pipeline");
        return 2;
    }

    private static int Alive()
    {
        SpikeLog.Info("Passo 0: o casco assinado esta' de pe'. Saindo.");
        return 0;
    }

    // ------------------------------------------------------------------
    // Passo 1 — risco 1: o event tap no F13, e o religamento depois do timeout
    // ------------------------------------------------------------------
    private static int RunTap()
    {
        Frameworks.EnsureLoaded();
        ObjCClasses.Warm();
        MainThread.Init();

        bool trusted = Accessibility.IsTrusted(prompt: true);
        SpikeLog.Info($"AXIsProcessTrusted = {trusted}");
        if (!trusted)
        {
            SpikeLog.Warn("Sem permissao de Acessibilidade. O dialogo do sistema deve ter "
                        + "subido; conceda e RODE DE NOVO (a permissao nao vale para o "
                        + "processo ja' em execucao).");
        }

        KeyTap.OnHotkey = () => SpikeLog.Info("*** handler do atalho rodou (fora da thread do tap) ***");
        if (!KeyTap.Start()) return 1;

        SpikeLog.Info("");
        SpikeLog.Info("ROTEIRO DESTA PERNA:");
        SpikeLog.Info("  1. aperte varias teclas: cada keyDown loga o keycode;");
        SpikeLog.Info("  2. aperte F13 com o TextEdit na frente: o log dispara e NADA e'");
        SpikeLog.Info("     digitado la' (o tap engoliu o evento);");
        SpikeLog.Info("  3. aperte F14 e depois qualquer tecla: tem de aparecer o WARN de");
        SpikeLog.Info("     timeout, o religamento, e a tecla seguinte voltando ao log.");
        SpikeLog.Info("  Ctrl+C encerra.");
        SpikeLog.Info("");

        CoreGraphics.CFRunLoopRun();   // nunca retorna
        KeyTap.Stop();
        return 0;
    }

    // ------------------------------------------------------------------
    // Passo 2 — risco 2: captura de audio
    // ------------------------------------------------------------------
    private static int RunAudio()
    {
        var samples = Capture();
        return samples.Length == 0 ? 1 : 0;
    }

    /// <summary>Grava e reporta pico/RMS. Devolve array vazio quando o audio veio mudo.</summary>
    private static float[] Capture()
    {
        SpikeLog.Info("fale alguma coisa AGORA...");
        var samples = QueueRecorder.Record(RecordSeconds);

        float peak = QueueRecorder.Peak(samples);
        float rms = QueueRecorder.Rms(samples);
        SpikeLog.Info($"capturadas {samples.Length} amostras "
                    + $"({samples.Length / QueueRecorder.SampleRate:F2} s) — "
                    + $"pico {peak:F4}, RMS {rms:F5}");

        if (QueueRecorder.LooksSilent(samples))
        {
            SpikeLog.Error("AUDIO VEIO MUDO — provavelmente permissao de microfone NEGADA.");
            SpikeLog.Error("Quando o microfone e' negado o macOS NAO devolve erro: devolve um "
                         + "fluxo de zeros. Nao vou mandar isso para o Whisper — transcrever "
                         + "silencio produz string vazia ou alucinacao, e a tarde iria embora "
                         + "depurando o Whisper por causa de um problema de permissao.");
            return Array.Empty<float>();
        }
        return samples;
    }

    // ------------------------------------------------------------------
    // Passo 3 — risco 3: Whisper, e a pergunta do Metal
    // ------------------------------------------------------------------
    private static int RunWhisper(string? wavPath)
    {
        SpikeTranscriber.HookNativeLog();
        var model = SpikeModel.EnsureBaseModel();

        float[] samples;
        if (wavPath != null)
        {
            samples = WavFile.ReadMono16(wavPath, out int rate);
            SpikeLog.Info($"audio do arquivo {wavPath}: {samples.Length} amostras a {rate} Hz "
                        + $"({samples.Length / (double)rate:F2} s), RMS {QueueRecorder.Rms(samples):F5}");
            if (rate != (int)QueueRecorder.SampleRate)
            {
                SpikeLog.Error($"o WAV esta' a {rate} Hz e o Whisper quer "
                             + $"{QueueRecorder.SampleRate} Hz. Reamostre antes:");
                SpikeLog.Error("  afconvert -f WAVE -d LEI16@16000 -c 1 entrada.aiff saida.wav");
                return 1;
            }
        }
        else
        {
            samples = Capture();
            if (samples.Length == 0) return 1;
        }

        using var transcriber = new SpikeTranscriber(model);
        var texto = transcriber.Transcribe(samples, out long wallMs);

        double audioSec = samples.Length / QueueRecorder.SampleRate;
        SpikeLog.Info("");
        SpikeLog.Info($"TEXTO: \"{texto}\"");
        SpikeLog.Info($"LATENCIA: {wallMs} ms de parede para {audioSec:F2} s de audio "
                    + $"(x{audioSec * 1000 / Math.Max(wallMs, 1):F1} tempo real)");
        SpikeLog.Info($"BACKEND (veredito do risco 3): {SpikeTranscriber.BackendVerdict()}");
        SpikeLog.Info("");
        return texto.Length == 0 ? 1 : 0;
    }

    // ------------------------------------------------------------------
    // Passo 4 — risco 4: injecao por CGEvent
    // ------------------------------------------------------------------
    private static int RunInject()
    {
        Frameworks.EnsureLoaded();
        MainThread.Init();

        // O tap sobe junto DE PROPOSITO: sem ele nao da' para provar que a marca da fonte
        // funciona, e um contador de auto-reconhecimento em zero significa que o caminho
        // nunca foi exercitado — a Fase 2 levaria um susto.
        bool tapUp = KeyTap.Start();
        if (!tapUp)
            SpikeLog.Warn("sem tap (falta Acessibilidade): o contador de auto-reconhecimento "
                        + "vai ficar em zero e o risco 4 fica PELA METADE provado.");

        EventInjector.Start();

        const string frase =
            "Esta e uma frase longa de teste do Matraca, com mais de duzentos caracteres, "
          + "escrita de proposito para exercitar o fatiamento em blocos e provar que o texto "
          + "chega inteiro e com os espacos preservados no aplicativo de destino.";

        SpikeLog.Info($"voce tem 5 segundos para clicar no app alvo (TextEdit, Terminal ou "
                    + $"Claude Code). Vou digitar {frase.Length} caracteres.");
        Thread.Sleep(5000);

        EventInjector.Enqueue(frase);
        EventInjector.Drain();

        SpikeLog.Info("");
        SpikeLog.Info($"o tap viu {KeyTap.OwnEventsSeen} eventos com a NOSSA marca e ignorou.");
        if (tapUp && KeyTap.OwnEventsSeen == 0)
            SpikeLog.Error("contador ZERO com o tap ativo: o auto-reconhecimento pela marca da "
                         + "fonte NAO foi exercitado. Nao conte o risco 4 como provado.");
        SpikeLog.Info("CONFIRA NO ALVO: o texto chegou INTEIRO e COM OS ESPACOS?");
        SpikeLog.Info("");

        EventInjector.Stop();
        KeyTap.Stop();
        return 0;
    }

    // ------------------------------------------------------------------
    // Passo 5 — risco 5: a janela WKWebView
    // ------------------------------------------------------------------
    private static int RunHud()
    {
        Frameworks.EnsureLoaded();
        ObjCClasses.Warm();
        MainThread.Init();

        HudWindow.Show();

        SpikeLog.Info("");
        SpikeLog.Info("ROTEIRO DESTA PERNA:");
        SpikeLog.Info("  1. da' para ver o que esta' ATRAS do HUD pelas regioes transparentes?");
        SpikeLog.Info("  2. clicar em cima do HUD atinge o app de baixo?");
        SpikeLog.Info("  3. a barra do canvas se MEXE? (se renderizou mas nao anima, o loop de");
        SpikeLog.Info("     render nao esta' recebendo tique — metade do risco 5)");
        SpikeLog.Info("  4. entre em tela cheia num app: o HUD continua visivel?");
        SpikeLog.Info("  Ctrl+C encerra.");
        SpikeLog.Info("");

        HudWindow.RunApp();   // nunca retorna
        return 0;
    }

    // ------------------------------------------------------------------
    // Passo 6 — o pipeline inteiro
    // ------------------------------------------------------------------
    private static int RunPipeline()
    {
        Frameworks.EnsureLoaded();
        ObjCClasses.Warm();
        MainThread.Init();

        SpikeTranscriber.HookNativeLog();
        var model = SpikeModel.EnsureBaseModel();
        var transcriber = new SpikeTranscriber(model);
        EventInjector.Start();

        bool trusted = Accessibility.IsTrusted(prompt: true);
        SpikeLog.Info($"AXIsProcessTrusted = {trusted}");

        KeyTap.OnHotkey = () =>
        {
            // Isto ja' roda FORA da thread do tap (o KeyTap enfileira). Gravar,
            // transcrever e digitar aqui dentro da thread do tap seria a lei 5 violada.
            var t0 = SpikeLog.ElapsedMs;
            var samples = Capture();
            var tGravou = SpikeLog.ElapsedMs;
            if (samples.Length == 0) return;

            var texto = transcriber.Transcribe(samples, out long wallMs);
            var tTranscreveu = SpikeLog.ElapsedMs;
            SpikeLog.Info($"TEXTO: \"{texto}\"");
            SpikeLog.Info($"BACKEND: {SpikeTranscriber.BackendVerdict()}");

            if (texto.Length == 0) { SpikeLog.Warn("texto vazio; nada a digitar."); return; }

            EventInjector.Enqueue(texto);
            EventInjector.Drain();

            SpikeLog.Info($"ETAPAS: gravou {tGravou - t0} ms | transcreveu {wallMs} ms | "
                        + $"digitou {SpikeLog.ElapsedMs - tTranscreveu} ms | "
                        + $"total {SpikeLog.ElapsedMs - t0} ms");
            SpikeLog.Info($"o tap viu {KeyTap.OwnEventsSeen} eventos com a nossa marca e ignorou.");
        };

        if (!KeyTap.Start()) return 1;

        SpikeLog.Info("");
        SpikeLog.Info("ROTEIRO DESTA PERNA (o criterio de aceite do cartao):");
        SpikeLog.Info("  clique no TextEdit, aperte F13, fale 3 s, e confira o texto.");
        SpikeLog.Info("  repita no Terminal e num app Electron (VS Code / Claude Code).");
        SpikeLog.Info("  O TEXTO PRECISA CHEGAR INTEIRO E COM OS ESPACOS — essa e' a");
        SpikeLog.Info("  sentinela de regressao herdada do Windows, com as mesmas palavras.");
        SpikeLog.Info("  Ctrl+C encerra.");
        SpikeLog.Info("");

        CoreGraphics.CFRunLoopRun();   // nunca retorna
        return 0;
    }
}
