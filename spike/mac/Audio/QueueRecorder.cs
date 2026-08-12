using System.Runtime.InteropServices;
using Matraca.MacSpike.Interop;

namespace Matraca.MacSpike.Audio;

/// <summary>
/// Grava do microfone por AudioQueue e entrega <c>float[]</c> mono a 16 kHz, que e' o que o
/// Whisper.net come.
///
/// O callback roda numa thread interna do AudioToolbox e faz DUAS coisas: copia os bytes e
/// re-enfileira o buffer. Qualquer trabalho a mais ali dentro (converter, transcrever,
/// logar em rajada) atrasa a captura e produz estouro de buffer.
/// </summary>
internal static unsafe class QueueRecorder
{
    public const double SampleRate = 16000;

    // Tres buffers de ~100 ms. Tres e' o minimo confortavel: enquanto um esta' sendo
    // preenchido pelo driver e outro esta' na nossa mao sendo copiado, o terceiro segura a
    // folga. 100 ms x 16000 amostras/s x 2 bytes = 3200 bytes por buffer.
    private const int BufferCount = 3;
    private const int BufferBytes = (int)(SampleRate * 0.100) * 2;

    private static readonly object Gate = new();
    private static readonly List<short> Captured = new();
    private static bool _capturing;

    /// <summary>
    /// Grava por <paramref name="seconds"/> segundos e devolve as amostras normalizadas em
    /// [-1, 1]. Sincrono de proposito: o spike nao tem VAD nem modos (isso e' Fase 2).
    /// </summary>
    public static float[] Record(double seconds)
    {
        Frameworks.EnsureLoaded();

        lock (Gate) { Captured.Clear(); _capturing = true; }

        var format = AudioStreamBasicDescription.Pcm16Mono(SampleRate);

        int st = AudioToolbox.AudioQueueNewInput(ref format, &OnBuffer, IntPtr.Zero,
                                                 IntPtr.Zero, IntPtr.Zero, 0, out var queue);
        if (st != 0)
            throw new InvalidOperationException(
                $"AudioQueueNewInput falhou: {AudioToolbox.Status(st)}");

        for (int i = 0; i < BufferCount; i++)
        {
            st = AudioToolbox.AudioQueueAllocateBuffer(queue, BufferBytes, out var buf);
            if (st != 0)
                throw new InvalidOperationException(
                    $"AudioQueueAllocateBuffer falhou: {AudioToolbox.Status(st)}");
            AudioToolbox.AudioQueueEnqueueBuffer(queue, buf, 0, IntPtr.Zero);
        }

        SpikeLog.Info($"gravando {seconds:F1} s a {SampleRate} Hz, mono, PCM16 "
                    + $"({BufferCount} buffers de {BufferBytes} bytes ~ 100 ms)...");

        st = AudioToolbox.AudioQueueStart(queue, IntPtr.Zero);
        if (st != 0)
            throw new InvalidOperationException($"AudioQueueStart falhou: {AudioToolbox.Status(st)}");

        Thread.Sleep((int)(seconds * 1000));

        AudioToolbox.AudioQueueStop(queue, true);
        lock (Gate) _capturing = false;
        AudioToolbox.AudioQueueDispose(queue, true);

        short[] pcm;
        lock (Gate) pcm = Captured.ToArray();

        var samples = new float[pcm.Length];
        for (int i = 0; i < pcm.Length; i++) samples[i] = pcm[i] / 32768f;
        return samples;
    }

    [UnmanagedCallersOnly]
    private static void OnBuffer(IntPtr userData, IntPtr queue, AudioQueueBuffer* buffer,
                                 IntPtr startTime, uint numPacketDescs, IntPtr packetDescs)
    {
        try
        {
            int bytes = (int)buffer->AudioDataByteSize;
            if (bytes > 0)
            {
                int count = bytes / 2;
                var span = new ReadOnlySpan<short>((void*)buffer->AudioData, count);
                lock (Gate) { if (_capturing) Captured.AddRange(span); }
            }

            // Devolver o MESMO ponteiro que veio. Sem isto a fila esvazia e a captura para
            // em silencio depois de tres buffers.
            lock (Gate) { if (!_capturing) return; }
            AudioToolbox.AudioQueueEnqueueBuffer(queue, buffer, 0, IntPtr.Zero);
        }
        catch (Exception ex)
        {
            try { SpikeLog.Error("callback do AudioQueue estourou", ex); } catch { }
        }
    }

    /// <summary>Pico absoluto das amostras.</summary>
    public static float Peak(float[] s)
    {
        float p = 0;
        foreach (var v in s) { float a = Math.Abs(v); if (a > p) p = a; }
        return p;
    }

    /// <summary>Valor eficaz (RMS) das amostras.</summary>
    public static float Rms(float[] s)
    {
        if (s.Length == 0) return 0;
        double acc = 0;
        foreach (var v in s) acc += (double)v * v;
        return (float)Math.Sqrt(acc / s.Length);
    }

    /// <summary>
    /// A checagem que separa duas causas de uma vez.
    ///
    /// Quando o microfone e' NEGADO, o macOS nao devolve erro: devolve um fluxo de ZEROS.
    /// Mandar silencio para o Whisper produz string vazia ou alucinacao, e ai' se perde a
    /// tarde depurando o Whisper por causa de um problema de permissao. RMS zero e' barato
    /// de medir e resolve a duvida na hora.
    /// </summary>
    public static bool LooksSilent(float[] s) => Rms(s) < 1e-6f;
}
