using System.Runtime.InteropServices;

namespace Matraca;

[StructLayout(LayoutKind.Sequential)]
internal struct WindowsMinMaxInfo
{
    public WindowsPoint Reserved;
    public WindowsPoint MaximumSize;
    public WindowsPoint MaximumPosition;
    public WindowsPoint MinimumTrackSize;
    public WindowsPoint MaximumTrackSize;
}
