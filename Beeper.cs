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

    /// <summary>Toca tons sinteticos em sequencia. volume 0..1.</summary>
    public static void Play((int freq, int ms)[] notes, float volume)
    {
        volume = Math.Clamp(volume, 0f, 1f);
        if (volume <= 0f || notes.Length == 0) return;

        byte[] wav;
        try { wav = BuildTonesWav(notes, volume); }
        catch (Exception ex) { Logger.Warn($"beep: falha ao gerar WAV: {ex.Message}"); return; }
        PlayBytes(wav);
    }

    /// <summary>Toca um arquivo de audio (wav/mp3/...) aplicando o volume (0..1).</summary>
    public static void PlaySoundFile(string path, float volume)
    {
        volume = Math.Clamp(volume, 0f, 1f);
        if (volume <= 0f) return;

        byte[] wav;
        try
        {
            using var reader = new AudioFileReader(path) { Volume = volume }; // decodifica + ganho
            using var mem = new MemoryStream();
            WaveFileWriter.WriteWavFileToStream(mem, reader.ToWaveProvider16()); // PCM16 -> WAV
            wav = mem.ToArray();
        }
        catch (Exception ex) { Logger.Warn($"beep: falha ao decodificar '{path}': {ex.Message}"); return; }
        PlayBytes(wav);
    }

    private static void PlayBytes(byte[] wav)
    {
        Task.Run(() =>
        {
            try
            {
                using var ms = new MemoryStream(wav);
                using var sp = new SoundPlayer(ms);
                sp.PlaySync();
            }
            catch (Exception ex) { Logger.Warn($"beep: falha ao tocar: {ex.Message}"); }
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

        int dataLen = samples.Count * 2;
        int byteRate = SampleRate * 2;
        using var mem = new MemoryStream();
        using var w = new BinaryWriter(mem);
        w.Write(Encoding.ASCII.GetBytes("RIFF"));
        w.Write(36 + dataLen);
        w.Write(Encoding.ASCII.GetBytes("WAVE"));
        w.Write(Encoding.ASCII.GetBytes("fmt "));
        w.Write(16);             // tamanho do chunk fmt
        w.Write((short)1);       // PCM
        w.Write((short)1);       // mono
        w.Write(SampleRate);
        w.Write(byteRate);
        w.Write((short)2);       // block align (mono 16-bit)
        w.Write((short)16);      // bits por amostra
        w.Write(Encoding.ASCII.GetBytes("data"));
        w.Write(dataLen);
        foreach (var s in samples) w.Write(s);
        w.Flush();
        return mem.ToArray();
    }
}
