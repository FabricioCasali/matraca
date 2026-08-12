namespace Matraca.MacSpike.Speech;

/// <summary>
/// Leitor minimo de WAV PCM 16 bits — so' o suficiente para alimentar a perna
/// <c>--whisper</c> a partir de um arquivo.
///
/// POR QUE ISTO EXISTE (nao estava no desenho): a prova do risco 3 — a linha de backend do
/// whisper.cpp e o tempo de parede — precisa ser rodavel por quem NAO tem permissao de
/// microfone concedida. Sem uma fonte de audio alternativa, <c>--whisper</c> so' rodaria com
/// o Fabricio na frente da maquina, e o risco 3 (a incerteza numero 1 do desenho) ficaria
/// "por provar" a toa.
///
/// Fala de verdade, sem microfone e sem rede, sai do proprio macOS:
///   say -v Luciana -o /tmp/fala.aiff "uma frase qualquer"
///   afconvert -f WAVE -d LEI16@16000 -c 1 /tmp/fala.aiff /tmp/fala.wav
///
/// Nao pretende ser um parser completo: aceita PCM 16 bits e converte estereo para mono
/// pela media. Morre com o spike.
/// </summary>
internal static class WavFile
{
    /// <summary>Le um WAV PCM16 e devolve as amostras mono em [-1, 1].</summary>
    public static float[] ReadMono16(string path, out int sampleRate)
    {
        using var fs = File.OpenRead(path);
        using var r = new BinaryReader(fs);

        if (new string(r.ReadChars(4)) != "RIFF") throw new InvalidDataException("nao e' RIFF");
        r.ReadUInt32();
        if (new string(r.ReadChars(4)) != "WAVE") throw new InvalidDataException("nao e' WAVE");

        short channels = 0, bits = 0;
        sampleRate = 0;
        byte[]? data = null;

        while (fs.Position < fs.Length - 8)
        {
            var id = new string(r.ReadChars(4));
            uint size = r.ReadUInt32();

            if (id == "fmt ")
            {
                long next = fs.Position + size;
                r.ReadUInt16();                 // formato (1 = PCM)
                channels = r.ReadInt16();
                sampleRate = (int)r.ReadUInt32();
                r.ReadUInt32();                 // byte rate
                r.ReadUInt16();                 // block align
                bits = r.ReadInt16();
                fs.Position = next;
            }
            else if (id == "data")
            {
                data = r.ReadBytes((int)size);
                break;
            }
            else
            {
                fs.Position += size + (size % 2);   // pedaços vao alinhados em 2 bytes
            }
        }

        if (data == null) throw new InvalidDataException("WAV sem pedaço 'data'");
        if (bits != 16) throw new NotSupportedException($"so' PCM 16 bits (veio {bits})");
        if (channels < 1) channels = 1;

        int frames = data.Length / 2 / channels;
        var samples = new float[frames];
        for (int f = 0; f < frames; f++)
        {
            int acc = 0;
            for (int c = 0; c < channels; c++)
            {
                int i = (f * channels + c) * 2;
                acc += (short)(data[i] | (data[i + 1] << 8));
            }
            samples[f] = acc / (float)channels / 32768f;
        }
        return samples;
    }
}
