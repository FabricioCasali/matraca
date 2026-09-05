using System.Runtime.InteropServices;
using System.Text;

namespace Matraca;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct WindowsOpenFileName
{
    public uint Size;
    public nint Owner;
    public nint Instance;

    [MarshalAs(UnmanagedType.LPWStr)]
    public string Filter;

    public nint CustomFilter;
    public uint MaximumCustomFilter;
    public uint FilterIndex;

    [MarshalAs(UnmanagedType.LPWStr)]
    public StringBuilder File;

    public uint MaximumFile;
    public nint FileTitle;
    public uint MaximumFileTitle;
    public nint InitialDirectory;

    [MarshalAs(UnmanagedType.LPWStr)]
    public string Title;

    public uint Flags;
    public ushort FileOffset;
    public ushort FileExtension;

    [MarshalAs(UnmanagedType.LPWStr)]
    public string DefaultExtension;

    public nint CustomData;
    public nint Hook;
    public nint TemplateName;
    public nint Reserved;
    public uint Reserved2;
    public uint FlagsEx;
}
