using System.Runtime.InteropServices;

namespace Matraca;

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate nint WindowsWindowProcedure(nint window, uint message, nint wParam, nint lParam);
