using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace Matraca;

internal static class WindowsFilePicker
{
    private const uint FileMustExist = 0x00001000;
    private const uint PathMustExist = 0x00000800;
    private const uint Explorer = 0x00080000;
    private const uint NoChangeDirectory = 0x00000008;
    private const uint DoNotAddToRecent = 0x02000000;
    private const int MaximumPathLength = 32768;

    public static string? PickModel(nint owner)
        => Pick(
            owner,
            "Selecionar modelo Whisper",
            "Modelos Whisper (*.bin)\0*.bin\0\0",
            "bin");

    public static string? PickSound(nint owner)
        => Pick(
            owner,
            "Selecionar som",
            "Sons WAV (*.wav)\0*.wav\0\0",
            "wav");

    private static string? Pick(nint owner, string title, string filter, string extension)
    {
        var file = new StringBuilder(MaximumPathLength);
        var dialog = new WindowsOpenFileName
        {
            Size = (uint)Marshal.SizeOf<WindowsOpenFileName>(),
            Owner = owner,
            Filter = filter,
            FilterIndex = 1,
            File = file,
            MaximumFile = (uint)file.Capacity,
            Title = title,
            Flags = FileMustExist | PathMustExist | Explorer | NoChangeDirectory | DoNotAddToRecent,
            DefaultExtension = extension,
        };

        if (WindowsNativeMethods.GetOpenFileName(ref dialog)) return file.ToString();

        uint error = WindowsNativeMethods.CommDlgExtendedError();
        if (error == 0) return null;
        throw new Win32Exception((int)error, $"Falha ao abrir o seletor de arquivo (0x{error:X4}).");
    }
}
