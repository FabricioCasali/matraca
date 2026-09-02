using System.Runtime.InteropServices;
using Matraca.Core;
using CoreConfig = Matraca.Core.Config;

namespace Matraca.Mac.Platform.Speech;

internal sealed class MacWhisperTranscriber : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IntPtr _session;

    internal MacWhisperTranscriber(CoreConfig config)
    {
        if (!File.Exists(config.ModelPath))
            throw new FileNotFoundException($"Modelo Whisper nao encontrado: {config.ModelPath}");

        string runtimeDirectory = Path.Combine(AppContext.BaseDirectory, "runtimes", "macos-arm64");
        string prompt = TranscriptionPromptBuilder.Build(config.Vocabulary, Logger.Warn);
        int result = MacWhisperNative.Open(
            runtimeDirectory,
            config.ModelPath,
            config.Language,
            prompt,
            config.Gpu == "cpu" ? 0 : 1,
            MacWhisperNative.LoggerCallback,
            out _session,
            out IntPtr error);
        if (result != 0)
            throw new InvalidOperationException(ReadAndFree(error, "Falha ao iniciar Whisper persistente."));

        Logger.Info($"Modelo e estado Whisper carregados: {config.ModelPath}"
            + (prompt.Length > 0 ? $" (vocabulario: {config.Vocabulary.Length} termos)" : ""));
    }

    internal ulong RunCount
    {
        get
        {
            _gate.Wait();
            try
            {
                IntPtr session = Volatile.Read(ref _session);
                return session == IntPtr.Zero ? 0 : MacWhisperNative.RunCount(session);
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    internal static Task<TranscriptionModel> CreateModelAsync(
        CoreConfig config,
        CancellationToken cancellationToken)
        => Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var transcriber = new MacWhisperTranscriber(config);
            return new TranscriptionModel(transcriber.TranscribeAsync, transcriber.Dispose);
        }, cancellationToken);

    internal async Task<string> TranscribeAsync(
        float[] samples,
        CancellationToken cancellationToken = default)
    {
        if (samples.Length == 0) return "";
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            IntPtr session = Volatile.Read(ref _session);
            ObjectDisposedException.ThrowIf(session == IntPtr.Zero, this);
            MacWhisperNative.ResetCancel(session);
            using CancellationTokenRegistration registration = cancellationToken.Register(
                static value => MacWhisperNative.Cancel((IntPtr)value!),
                session);
            cancellationToken.ThrowIfCancellationRequested();
            return await Task.Run(() =>
            {
                int result = MacWhisperNative.Transcribe(
                    session,
                    samples,
                    samples.Length,
                    out IntPtr text,
                    out IntPtr error);
                if (result != 0)
                {
                    string message = ReadAndFree(error, $"Whisper falhou ao transcrever (codigo {result}).");
                    cancellationToken.ThrowIfCancellationRequested();
                    throw new InvalidOperationException(message);
                }
                string transcription = ReadAndFree(text, "").Trim();
                cancellationToken.ThrowIfCancellationRequested();
                return transcription;
            }, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string ReadAndFree(IntPtr value, string fallback)
    {
        if (value == IntPtr.Zero) return fallback;
        try { return Marshal.PtrToStringUTF8(value) ?? fallback; }
        finally { MacWhisperNative.FreeString(value); }
    }

    public void Dispose()
    {
        _gate.Wait();
        try
        {
            IntPtr session = Interlocked.Exchange(ref _session, IntPtr.Zero);
            if (session != IntPtr.Zero) MacWhisperNative.Close(session);
        }
        finally
        {
            _gate.Release();
        }
    }
}
