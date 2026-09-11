using System.Runtime.InteropServices;

namespace MacUpdateProbe;

internal static class Program
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GateCallback(int operation);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int RunProbe(GateCallback callback);

    private static int Main(string[] args)
    {
        if (args.SequenceEqual(new[] { "--gate-test" }))
            return GateTests.Run();
        if (!OperatingSystem.IsMacOS())
        {
            Console.Error.WriteLine("O probe nativo exige macOS 15+ arm64. Apenas --gate-test é portátil.");
            return 2;
        }

        // Same host family as Matraca.Mac: ordinary net8.0 apphost, no Xamarin/MAUI.
        // Blocking native call keeps AppKit on the original main thread.
        var gate = new InstallGate();
        GateCallback callback = operation =>
        {
            if (operation == 7) return typeof(Program).Assembly.GetName().Version!.Major;
            try { return gate.Handle(operation) ? 1 : 0; }
            catch { return 0; } // Never unwind a managed exception through Objective-C.
        };
        nint library = NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, "libMacUpdateProbe.dylib"));
        var run = Marshal.GetDelegateForFunctionPointer<RunProbe>(NativeLibrary.GetExport(library, "mp_run"));
        int result = run(callback);
        GC.KeepAlive(callback);
        // The library and delegate remain alive throughout NSApplication.run.
        return result;
    }
}
