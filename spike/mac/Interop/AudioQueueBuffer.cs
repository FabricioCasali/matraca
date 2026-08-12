using System.Runtime.InteropServices;

namespace Matraca.MacSpike.Interop;

/// <summary>
/// AudioQueueBuffer, exatamente na ordem do cabecalho. O callback recebe um PONTEIRO para
/// esta struct; nunca copie e devolva a copia — o <c>AudioQueueEnqueueBuffer</c> quer de
/// volta o mesmo ponteiro que veio.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct AudioQueueBuffer
{
    public uint AudioDataBytesCapacity;
    public IntPtr AudioData;
    public uint AudioDataByteSize;
    public IntPtr UserData;
    public uint PacketDescriptionCapacity;
    public IntPtr PacketDescriptions;
    public uint PacketDescriptionCount;
}
