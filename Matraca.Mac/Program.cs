using Matraca.Core;
using Matraca.Mac.Platform;
using Matraca.Mac.Platform.Interop;

namespace Matraca.Mac;

internal static class Program
{
    private static int Main()
    {
        if (!OperatingSystem.IsMacOS())
            throw new PlatformNotSupportedException("Matraca.Mac requires macOS.");

        Logger.Initialize(AppPaths.Current());
        try
        {
            Frameworks.EnsureLoaded();
            ObjCClasses.Warm();
            using var pool = AutoreleasePool.New();

            var application = MacApplication.Shared();
            application.ConfigureAsAccessory();
            using var statusItem = new MacStatusItem(application);

            Logger.Info("Matraca.Mac iniciado como app Accessory.");
            application.Run();
            return 0;
        }
        catch (Exception exception)
        {
            Logger.Error("Falha fatal no Matraca.Mac", exception);
            return 1;
        }
    }
}
