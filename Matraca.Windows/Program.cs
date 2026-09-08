using Whisper.net.LibraryLoader;

namespace Matraca;

internal static class Program
{
    internal static void ApplyRuntimePreference(string gpu)
    {
        try
        {
            List<RuntimeLibrary>? order = gpu switch
            {
                "cpu" => new() { RuntimeLibrary.Cpu, RuntimeLibrary.CpuNoAvx },
                "gpu" => new() { RuntimeLibrary.Vulkan, RuntimeLibrary.Cpu, RuntimeLibrary.CpuNoAvx },
                _ => null,
            };
            if (order != null)
            {
                RuntimeOptions.RuntimeLibraryOrder = order;
                Logger.Info($"Preferência de runtime: {gpu} -> [{string.Join(", ", order)}]");
            }
        }
        catch (Exception exception)
        {
            Logger.Error("Falha ao aplicar preferência de runtime", exception);
        }
    }

    [STAThread]
    private static void Main(string[] args)
    {
        Logger.Initialize(WindowsConfig.Paths);

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
            catch (AbandonedMutexException) { }
        }

        try
        {
            if (!WindowsOleScope.TryEnter(out WindowsOleScope? oleScope))
                throw new InvalidOperationException("Nao foi possivel inicializar OLE na thread principal.");
            using (oleScope)
            using (var application = new WindowsApplication())
                application.Run();
        }
        catch (Exception exception)
        {
            Logger.Error("Falha fatal", exception);
            WindowsUiMessages messages = WindowsUiMessages.Current();
            WindowsNativeMethods.MessageBox(
                nint.Zero,
                messages.FatalError,
                "Matraca",
                WindowsNativeMethods.MbOk | WindowsNativeMethods.MbIconError);
        }
    }

    private static void RunLiveTest(string wavPath)
    {
        try
        {
            var config = WindowsConfig.Load();
            ApplyRuntimePreference(config.Gpu);
            using var reader = new NAudio.Wave.WaveFileReader(wavPath);
            var bytes = new byte[reader.Length];
            int read = reader.Read(bytes, 0, bytes.Length);
            var samples = new float[read / 2];
            for (int index = 0; index < samples.Length; index++)
                samples[index] = BitConverter.ToInt16(bytes, index * 2) / 32768f;

            using var transcriber = new Transcriber(config.ModelPath, config.Language, config.Vocabulary);
            var detector = new VoiceActivityDetector();
            int segmentIndex = 0;
            detector.SegmentReady += segment =>
            {
                int current = ++segmentIndex;
                string text = transcriber.TranscribeAsync(segment).GetAwaiter().GetResult();
                Logger.Info($"[LIVE-TESTE] chunk {current} (~{segment.Length / 16000.0:F1}s): \"{text.Trim()}\"");
            };
            Logger.Info($"[LIVE-TESTE] silenceMs={config.SilenceMs} threshold={config.EffectiveVadThreshold} phraseMax={config.PhraseMaxSeconds}s");
            detector.Start(config.EffectiveVadThreshold, config.SilenceMs, config.PhraseMaxSeconds);
            for (int offset = 0; offset < samples.Length; offset += VoiceActivityDetector.FrameSampleCount)
            {
                int length = Math.Min(VoiceActivityDetector.FrameSampleCount, samples.Length - offset);
                detector.Feed(samples.AsSpan(offset, length));
            }
            detector.Stop();
            Logger.Info($"[LIVE-TESTE] total de chunks: {segmentIndex}");
        }
        catch (Exception exception)
        {
            Logger.Error("[LIVE-TESTE] Falhou", exception);
        }
    }

    private static void RunTranscribeTest(string wavPath)
    {
        try
        {
            var config = WindowsConfig.Load();
            ApplyRuntimePreference(config.Gpu);
            Whisper.net.Logger.LogProvider.AddLogger((level, message) =>
                Logger.Info($"[whisper:{level}] {message?.Trim()}"));
            Logger.Info($"[TESTE] Transcrevendo {wavPath} (gpu={config.Gpu})...");
            using var reader = new NAudio.Wave.WaveFileReader(wavPath);
            Logger.Info($"[TESTE] WAV: {reader.WaveFormat}");
            var bytes = new byte[reader.Length];
            int read = reader.Read(bytes, 0, bytes.Length);
            var samples = new float[read / 2];
            for (int index = 0; index < samples.Length; index++)
                samples[index] = BitConverter.ToInt16(bytes, index * 2) / 32768f;

            using var transcriber = new Transcriber(config.ModelPath, config.Language, config.Vocabulary);
            for (int run = 1; run <= 2; run++)
            {
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                string text = transcriber.TranscribeAsync(samples).GetAwaiter().GetResult();
                stopwatch.Stop();
                Logger.Info($"[TESTE] run {run}: {stopwatch.ElapsedMilliseconds} ms -> \"{text}\"");
            }
        }
        catch (Exception exception)
        {
            Logger.Error("[TESTE] Falhou", exception);
        }
    }
}
