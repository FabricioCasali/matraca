using System.Media;
using System.Text;
using NAudio.Wave;

namespace Matraca;

/// <summary>
/// Toca os sons de inicio/fim de gravacao. Dois caminhos:
///  - tons sinteticos (gerados em memoria, volume pela amplitude);
///  - arquivo de audio do usuario (.wav/.mp3/...), decodificado via NAudio com volume aplicado.
/// Em ambos, a reproducao final e' via System.Media.SoundPlayer (robusto e sem depender de
/// inicializar um device de saida NAudio).
/// </summary>
internal static class Beeper
{
    private const int SampleRate = 44100;

    // Quantas reproducoes estao no ar e quando a ultima terminou. A janela de mute da gravacao
    // era calculada so' pela duracao do som, o que subestima: entre mandar tocar e o som sair de
    // fato ha' o Task.Run, a decodificacao do arquivo e o buffer da placa. Com um mp3 mais longo
    // essa latencia jogava o fim do som pra depois da janela e ele vazava pro audio gravado.
    // Com isto o mute acompanha a reproducao real em vez de adivinhar.
    private static int _playing;
    private static long _quietSinceTick = Environment.TickCount64;

    /// <summary>Folga apos o som terminar: cobre o buffer da placa e o eco curto do ambiente.</summary>
    public const int GuardMs = 150;

    /// <summary>Ha' som de bip tocando agora.</summary>
    public static bool IsPlaying => Volatile.Read(ref _playing) > 0;

    /// <summary>Ainda estamos dentro da sombra do bip (tocando ou ha' menos de guardMs que parou).</summary>
    public static bool InBeepShadow(int guardMs)
        => IsPlaying || (Environment.TickCount64 - Interlocked.Read(ref _quietSinceTick)) < guardMs;

    /// <summary>
    /// Toca tons sinteticos em sequencia. volume 0..1.
    /// Devolve a duracao do som em ms (0 se nao tocou) — quem grava usa isso p/ descartar
    /// o trecho de audio contaminado pelo proprio bip vindo do alto-falante.
    /// </summary>
    public static int Play((int freq, int ms)[] notes, float volume)
    {
        volume = Math.Clamp(volume, 0f, 1f);
        if (volume <= 0f || notes.Length == 0) return 0;

        byte[] wav;
        try { wav = BuildTonesWav(notes, volume); }
        catch (Exception ex) { Logger.Warn($"beep: falha ao gerar WAV: {ex.Message}"); return 0; }
        PlayBytes(wav);
        return notes.Sum(n => n.ms);
    }

    /// <summary>
    /// Toca um arquivo de audio (wav/mp3/...) aplicando o volume (0..1). Devolve a duracao do
    /// trecho que realmente soa, em ms.
    ///
    /// A duracao devolvida vira a janela de audio que a gravacao descarta, entao usar o tamanho
    /// do arquivo seria caro: mp3 costuma vir com silencio no fim (e o encoder ainda soma o seu
    /// padding), e um arquivo de 3s cujo som dura 0,7s comeria 3s de fala do usuario. Aqui o
    /// silencio final e' cortado antes de tocar, entao a janela reflete o som de verdade.
    /// </summary>
    public static int PlaySoundFile(string path, float volume)
    {
        volume = Math.Clamp(volume, 0f, 1f);
        if (volume <= 0f) return 0;

        byte[] wav;
        int durationMs;
        try
        {
            using var reader = new AudioFileReader(path) { Volume = volume }; // decodifica + ganho
            int rate = reader.WaveFormat.SampleRate, channels = reader.WaveFormat.Channels;

            var pcm = ReadAll(reader.ToWaveProvider16());
            int frames = TrimTrailingSilence(pcm, channels, rate);
            durationMs = (int)(1000L * frames / rate);
            wav = BuildWav(pcm, frames * channels * 2, rate, channels);

            int fileMs = (int)reader.TotalTime.TotalMilliseconds;
            if (fileMs - durationMs > 250)
                Logger.Info($"beep: '{Path.GetFileName(path)}' tem {fileMs}ms de arquivo mas {durationMs}ms de som; " +
                            "o silencio do fim foi cortado.");
        }
        catch (Exception ex) { Logger.Warn($"beep: falha ao decodificar '{path}': {ex.Message}"); return 0; }
        PlayBytes(wav);
        return durationMs;
    }

    private static byte[] ReadAll(IWaveProvider provider)
    {
        using var mem = new MemoryStream();
        var buf = new byte[16384];
        int read;
        while ((read = provider.Read(buf, 0, buf.Length)) > 0) mem.Write(buf, 0, read);
        return mem.ToArray();
    }

    /// <summary>
    /// Quantos frames manter ate' o fim do som audivel. O limiar e' relativo ao pico (e nao
    /// absoluto) porque o ganho ja' foi aplicado: com volume 0,2 um limiar fixo cortaria musica.
    /// </summary>
    private static int TrimTrailingSilence(byte[] pcm, int channels, int rate)
    {
        int totalFrames = pcm.Length / 2 / Math.Max(1, channels);
        if (totalFrames == 0) return 0;

        short peak = 0;
        for (int i = 0; i < pcm.Length; i += 2)
        {
            short s = BitConverter.ToInt16(pcm, i);
            short abs = s == short.MinValue ? short.MaxValue : Math.Abs(s);
            if (abs > peak) peak = abs;
        }
        if (peak == 0) return 0;                       // arquivo mudo: nao ha' o que descartar
        int floor = Math.Max(peak / 100, 16);          // 1% do pico

        int lastFrame = totalFrames - 1;
        while (lastFrame >= 0)
        {
            bool audible = false;
            for (int c = 0; c < channels && !audible; c++)
            {
                int idx = (lastFrame * channels + c) * 2;
                short s = BitConverter.ToInt16(pcm, idx);
                if ((s == short.MinValue ? short.MaxValue : Math.Abs(s)) >= floor) audible = true;
            }
            if (audible) break;
            lastFrame--;
        }
        if (lastFrame < 0) return 0;

        int tail = rate * 30 / 1000;                   // 30ms de cauda p/ nao cortar o decay
        return Math.Min(totalFrames, lastFrame + 1 + tail);
    }

    private static void PlayBytes(byte[] wav)
    {
        Interlocked.Increment(ref _playing);   // ja' conta como "tocando": o Task pode demorar a entrar
        Task.Run(() =>
        {
            try
            {
                using var ms = new MemoryStream(wav);
                using var sp = new SoundPlayer(ms);
                sp.PlaySync();
            }
            catch (Exception ex) { Logger.Warn($"beep: falha ao tocar: {ex.Message}"); }
            finally
            {
                Interlocked.Exchange(ref _quietSinceTick, Environment.TickCount64);
                Interlocked.Decrement(ref _playing);
            }
        });
    }

    private static byte[] BuildTonesWav((int freq, int ms)[] notes, float volume)
    {
        var samples = new List<short>();
        foreach (var (freq, ms) in notes)
        {
            int n = SampleRate * ms / 1000;
            int fade = Math.Min(n / 2, SampleRate * 5 / 1000); // ~5ms attack/decay (evita "click")
            for (int i = 0; i < n; i++)
            {
                double env = 1.0;
                if (i < fade) env = (double)i / fade;
                else if (i >= n - fade) env = (double)(n - i) / fade;
                double s = Math.Sin(2 * Math.PI * freq * i / SampleRate) * volume * env;
                samples.Add((short)(s * short.MaxValue));
            }
        }

        var pcm = new byte[samples.Count * 2];
        for (int i = 0; i < samples.Count; i++)
            BitConverter.GetBytes(samples[i]).CopyTo(pcm, i * 2);
        return BuildWav(pcm, pcm.Length, SampleRate, 1);
    }

    /// <summary>Empacota PCM16 num WAV em memoria (os primeiros dataLen bytes de pcm).</summary>
    private static byte[] BuildWav(byte[] pcm, int dataLen, int sampleRate, int channels)
    {
        dataLen = Math.Clamp(dataLen, 0, pcm.Length);
        int blockAlign = channels * 2;
        using var mem = new MemoryStream(44 + dataLen);
        using var w = new BinaryWriter(mem);
        w.Write(Encoding.ASCII.GetBytes("RIFF"));
        w.Write(36 + dataLen);
        w.Write(Encoding.ASCII.GetBytes("WAVE"));
        w.Write(Encoding.ASCII.GetBytes("fmt "));
        w.Write(16);                        // tamanho do chunk fmt
        w.Write((short)1);                  // PCM
        w.Write((short)channels);
        w.Write(sampleRate);
        w.Write(sampleRate * blockAlign);   // byte rate
        w.Write((short)blockAlign);
        w.Write((short)16);                 // bits por amostra
        w.Write(Encoding.ASCII.GetBytes("data"));
        w.Write(dataLen);
        w.Write(pcm, 0, dataLen);
        w.Flush();
        return mem.ToArray();
    }
}
