using System.Runtime.InteropServices;

namespace Matraca.MacSpike.Interop;

/// <summary>
/// AudioStreamBasicDescription — a descricao do formato que o AudioQueue vai entregar.
///
/// Campo a campo, exatamente na ordem do cabecalho (a struct e' passada por valor; trocar a
/// ordem nao da' erro, da' formato errado).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct AudioStreamBasicDescription
{
    public double SampleRate;
    public uint FormatID;
    public uint FormatFlags;
    public uint BytesPerPacket;
    public uint FramesPerPacket;
    public uint BytesPerFrame;
    public uint ChannelsPerFrame;
    public uint BitsPerChannel;
    public uint Reserved;

    /// <summary>'lpcm' — kAudioFormatLinearPCM.</summary>
    public const uint FormatLinearPCM = 0x6C70636D;

    /// <summary>kAudioFormatFlagIsSignedInteger | kAudioFormatFlagIsPacked.</summary>
    public const uint FlagsPCM16 = 0xC;

    /// <summary>
    /// PCM 16 bits, mono, na taxa pedida — o formato que o Whisper quer (16 kHz).
    ///
    /// POR QUE 16 kHz DIRETO DO AudioQueue, e nao 48 kHz reamostrados por nos: no Apple
    /// Silicon o hardware do microfone roda a 48 kHz, e o AudioQueue faz a reamostragem por
    /// conta propria. AudioUnit/HAL entregariam 48 kHz crus e a conversao viraria codigo
    /// nosso. Registrado aqui para ninguem "melhorar" isto depois.
    /// </summary>
    public static AudioStreamBasicDescription Pcm16Mono(double sampleRate) => new()
    {
        SampleRate = sampleRate,
        FormatID = FormatLinearPCM,
        FormatFlags = FlagsPCM16,
        BytesPerPacket = 2,
        FramesPerPacket = 1,
        BytesPerFrame = 2,
        ChannelsPerFrame = 1,
        BitsPerChannel = 16,
        Reserved = 0,
    };
}
