using System.Runtime.InteropServices;
using System.Text;
using Matraca.Mac.Platform.Audio;

namespace Matraca.Mac.Platform.Interop;

internal static unsafe class ExtAudioFileDecoder
{
    private const uint PropertyFileDataFormat = 0x66666D74; // 'ffmt'
    private const uint PropertyClientDataFormat = 0x63666D74; // 'cfmt'
    private const uint PropertyFileLengthFrames = 0x2366726D; // '#frm'
    private const uint FormatLinearPcm = 0x6C70636D; // 'lpcm'
    private const uint FlagsPcm16 = 0xC;
    private const int ReadFrames = 4096;
    private const string AudioToolboxFramework =
        "/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox";
    private const string CoreFoundationFramework =
        "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    public static byte[] DecodeToTrimmedWave(
        string path,
        int maximumDurationMilliseconds,
        int maximumDecodedBytes)
    {
        IntPtr url = CreateFileUrl(path);
        IntPtr audioFile = IntPtr.Zero;
        try
        {
            ThrowIfFailed("ExtAudioFileOpenURL", ExtAudioFileOpenURL(url, out audioFile));
            AudioStreamBasicDescription source = GetFormat(audioFile);
            ValidateFormat(source);

            int sampleRate = checked((int)Math.Round(source.SampleRate));
            int channels = checked((int)source.ChannelsPerFrame);
            int bytesPerFrame = checked(channels * sizeof(short));
            long declaredFrames = GetFrameLength(audioFile);
            if (declaredFrames > (long)sampleRate * maximumDurationMilliseconds / 1000)
                throw new InvalidDataException(
                    $"duracao excede o limite de {maximumDurationMilliseconds / 1000}s para feedback sonoro");

            var client = new AudioStreamBasicDescription
            {
                SampleRate = sampleRate,
                FormatId = FormatLinearPcm,
                FormatFlags = FlagsPcm16,
                BytesPerPacket = (uint)bytesPerFrame,
                FramesPerPacket = 1,
                BytesPerFrame = (uint)bytesPerFrame,
                ChannelsPerFrame = (uint)channels,
                BitsPerChannel = 16,
            };
            uint formatSize = (uint)sizeof(AudioStreamBasicDescription);
            ThrowIfFailed(
                "ExtAudioFileSetProperty(ClientDataFormat)",
                ExtAudioFileSetProperty(
                    audioFile,
                    PropertyClientDataFormat,
                    formatSize,
                    &client));

            byte[] pcm = ReadPcm(audioFile, channels, bytesPerFrame, maximumDecodedBytes);
            int totalFrames = pcm.Length / bytesPerFrame;
            if (totalFrames > (long)sampleRate * maximumDurationMilliseconds / 1000)
                throw new InvalidDataException(
                    $"duracao excede o limite de {maximumDurationMilliseconds / 1000}s para feedback sonoro");
            int audibleFrames = MacSoundWave.AudibleFrameCount(pcm, channels, sampleRate);
            if (audibleFrames == 0)
                throw new InvalidDataException("o arquivo de audio nao contem som audivel");
            return MacSoundWave.CreateFromPcm16(pcm, audibleFrames, sampleRate, channels);
        }
        finally
        {
            if (audioFile != IntPtr.Zero) ExtAudioFileDispose(audioFile);
            CFRelease(url);
        }
    }

    private static byte[] ReadPcm(
        IntPtr audioFile,
        int channels,
        int bytesPerFrame,
        int maximumDecodedBytes)
    {
        var readBuffer = new byte[checked(ReadFrames * bytesPerFrame)];
        using var decoded = new MemoryStream();
        fixed (byte* data = readBuffer)
        {
            while (true)
            {
                uint frames = ReadFrames;
                var buffers = new AudioBufferList
                {
                    NumberBuffers = 1,
                    Buffer = new AudioBuffer
                    {
                        NumberChannels = (uint)channels,
                        DataByteSize = (uint)readBuffer.Length,
                        Data = (IntPtr)data,
                    },
                };
                ThrowIfFailed("ExtAudioFileRead", ExtAudioFileRead(audioFile, ref frames, &buffers));
                if (frames == 0) break;

                int bytesRead = checked((int)frames * bytesPerFrame);
                if (decoded.Length + bytesRead > maximumDecodedBytes)
                    throw new InvalidDataException(
                        $"audio decodificado excede o limite de {maximumDecodedBytes / 1024 / 1024} MB");
                decoded.Write(readBuffer, 0, bytesRead);
            }
        }
        return decoded.ToArray();
    }

    private static AudioStreamBasicDescription GetFormat(IntPtr audioFile)
    {
        AudioStreamBasicDescription format;
        uint size = (uint)sizeof(AudioStreamBasicDescription);
        ThrowIfFailed(
            "ExtAudioFileGetProperty(FileDataFormat)",
            ExtAudioFileGetProperty(audioFile, PropertyFileDataFormat, ref size, &format));
        return format;
    }

    private static long GetFrameLength(IntPtr audioFile)
    {
        long frames;
        uint size = sizeof(long);
        ThrowIfFailed(
            "ExtAudioFileGetProperty(FileLengthFrames)",
            ExtAudioFileGetProperty(audioFile, PropertyFileLengthFrames, ref size, &frames));
        return Math.Max(0, frames);
    }

    private static void ValidateFormat(AudioStreamBasicDescription format)
    {
        if (!double.IsFinite(format.SampleRate) || format.SampleRate is < 8000 or > 192000)
            throw new InvalidDataException($"taxa de amostragem nao suportada: {format.SampleRate}");
        if (format.ChannelsPerFrame is < 1 or > 8)
            throw new InvalidDataException(
                $"quantidade de canais nao suportada: {format.ChannelsPerFrame}");
    }

    private static IntPtr CreateFileUrl(string path)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(Path.GetFullPath(path));
        fixed (byte* pointer = bytes)
        {
            IntPtr url = CFURLCreateFromFileSystemRepresentation(
                IntPtr.Zero,
                pointer,
                bytes.Length,
                isDirectory: false);
            return url != IntPtr.Zero
                ? url
                : throw new InvalidOperationException("CFURL nao aceitou o caminho do som");
        }
    }

    private static void ThrowIfFailed(string operation, int status)
    {
        if (status != 0)
            throw new InvalidOperationException(
                $"{operation} falhou: {AudioToolbox.DescribeStatus(status)}");
    }

    [DllImport(AudioToolboxFramework)]
    private static extern int ExtAudioFileOpenURL(IntPtr url, out IntPtr audioFile);

    [DllImport(AudioToolboxFramework)]
    private static extern int ExtAudioFileGetProperty(
        IntPtr audioFile,
        uint propertyId,
        ref uint propertyDataSize,
        void* propertyData);

    [DllImport(AudioToolboxFramework)]
    private static extern int ExtAudioFileSetProperty(
        IntPtr audioFile,
        uint propertyId,
        uint propertyDataSize,
        void* propertyData);

    [DllImport(AudioToolboxFramework)]
    private static extern int ExtAudioFileRead(
        IntPtr audioFile,
        ref uint frameCount,
        AudioBufferList* data);

    [DllImport(AudioToolboxFramework)]
    private static extern int ExtAudioFileDispose(IntPtr audioFile);

    [DllImport(CoreFoundationFramework)]
    private static extern IntPtr CFURLCreateFromFileSystemRepresentation(
        IntPtr allocator,
        byte* buffer,
        nint bufferLength,
        [MarshalAs(UnmanagedType.I1)] bool isDirectory);

    [DllImport(CoreFoundationFramework)]
    private static extern void CFRelease(IntPtr value);
}
