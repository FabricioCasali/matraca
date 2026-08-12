using System.Runtime.InteropServices;

namespace Matraca.MacSpike.Interop;

/// <summary>
/// AudioQueue — a captura de microfone.
///
/// POR QUE AudioQueue e nao AudioUnit/HAL (registre, para ninguem "melhorar" depois): no
/// Apple Silicon o microfone roda a 48 kHz no hardware. O AudioQueue reamostra para os
/// 16 kHz que o Whisper quer por conta propria; AudioUnit entregaria 48 kHz crus e a
/// conversao viraria codigo nosso. AudioQueue e' de longe o menor codigo.
///
/// Plano B, se um dia isto atrapalhar: AVAudioEngine com installTapOnBus: — funciona, mas e'
/// bem mais superficie de ObjC e o callback e' baseado em BLOCO, que e' justamente o que o
/// padrao de interop deste projeto evita.
/// </summary>
internal static unsafe class AudioToolbox
{
    private const string AT = "/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox";

    /// <summary>
    /// O parametro <c>callback</c> e' o AudioQueueInputCallback:
    /// <c>void (*)(void*, AudioQueueRef, AudioQueueBufferRef, const AudioTimeStamp*, UInt32,
    /// const AudioStreamPacketDescription*)</c>.
    ///
    /// Com <c>callbackRunLoop</c> em NULL ele roda numa thread INTERNA do AudioToolbox — e e'
    /// assim que queremos: o callback so' copia bytes e re-enfileira o buffer, sem tocar em
    /// nada de UI e sem depender da run loop principal, que o event tap ja' ocupa.
    /// </summary>
    [DllImport(AT)]
    public static extern int AudioQueueNewInput(
        ref AudioStreamBasicDescription format,
        delegate* unmanaged<IntPtr, IntPtr, AudioQueueBuffer*, IntPtr, uint, IntPtr, void> callback,
        IntPtr userData, IntPtr callbackRunLoop, IntPtr callbackRunLoopMode, uint flags,
        out IntPtr outQueue);

    [DllImport(AT)]
    public static extern int AudioQueueAllocateBuffer(IntPtr queue, uint bufferByteSize,
                                                      out AudioQueueBuffer* outBuffer);

    [DllImport(AT)]
    public static extern int AudioQueueEnqueueBuffer(IntPtr queue, AudioQueueBuffer* buffer,
                                                     uint numPacketDescs, IntPtr packetDescs);

    [DllImport(AT)]
    public static extern int AudioQueueStart(IntPtr queue, IntPtr startTime);

    [DllImport(AT)]
    public static extern int AudioQueueStop(IntPtr queue, [MarshalAs(UnmanagedType.I1)] bool immediate);

    [DllImport(AT)]
    public static extern int AudioQueueDispose(IntPtr queue, [MarshalAs(UnmanagedType.I1)] bool immediate);

    /// <summary>
    /// OSStatus e' um codigo de 4 bytes que quase sempre e' um FourCC legivel ('!dev',
    /// 'fmt?'). Imprimir os dois — numero e letras — poupa uma busca no Google por erro.
    /// </summary>
    public static string Status(int status)
    {
        if (status == 0) return "ok";
        var b = BitConverter.GetBytes(status);
        Array.Reverse(b);
        bool printable = b.All(c => c >= 0x20 && c < 0x7F);
        return printable ? $"{status} ('{System.Text.Encoding.ASCII.GetString(b)}')" : status.ToString();
    }
}
