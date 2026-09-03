using System.Runtime.InteropServices;
using Matraca.Core;
using Matraca.Mac.Platform.Interop;

namespace Matraca.Mac.Platform;

internal sealed class MacTerminationHandshake : IDisposable
{
    // Accepted text gets this long to drain before AppKit is allowed to end the process.
    public static readonly TimeSpan DeliveryGracePeriod = TimeSpan.FromSeconds(5);

    private const nuint TerminateNow = 1;
    private const nuint TerminateLater = 2;
    private static MacTerminationHandshake? _current;

    private readonly MacApplication _application;
    private readonly BoundedShutdownCoordinator _shutdown;
    private IntPtr _delegate;
    private int _nativeReplyStarted;
    private int _disposed;

    public unsafe MacTerminationHandshake(
        MacApplication application,
        Func<CancellationToken, Task> shutdown)
    {
        _application = application ?? throw new ArgumentNullException(nameof(application));
        _shutdown = new BoundedShutdownCoordinator(
            shutdown,
            DeliveryGracePeriod);

        if (Interlocked.CompareExchange(ref _current, this, null) != null)
            throw new InvalidOperationException("An application termination delegate is already installed.");

        try
        {
            IntPtr cls = ObjCClassBuilder
                .Create("MatracaApplicationDelegate", ObjCClasses.NSObject)
                .AddMethod(
                    ObjCSelectors.ApplicationShouldTerminate,
                    (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, nuint>)&ApplicationShouldTerminate,
                    "Q@:@")
                .Register();
            _delegate = ObjC.New(cls);
            _application.SetDelegate(_delegate);
        }
        catch
        {
            Interlocked.CompareExchange(ref _current, null, this);
            if (_delegate != IntPtr.Zero) ObjC.SendVoid(_delegate, ObjCSelectors.Release);
            _delegate = IntPtr.Zero;
            throw;
        }
    }

    public Task<bool> RequestShutdownAsync() => _shutdown.RequestShutdownAsync();

    [UnmanagedCallersOnly]
    private static nuint ApplicationShouldTerminate(
        IntPtr self,
        IntPtr command,
        IntPtr sender)
    {
        try
        {
            MacTerminationHandshake? current = Volatile.Read(ref _current);
            if (current == null || current._delegate != self) return TerminateNow;
            current.BeginNativeTermination();
            return TerminateLater;
        }
        catch (Exception exception)
        {
            try { Logger.Error("Falha ao iniciar o encerramento nativo", exception); } catch { }
            return TerminateNow;
        }
    }

    private void BeginNativeTermination()
    {
        if (Interlocked.Exchange(ref _nativeReplyStarted, 1) != 0) return;
        _ = ReplyWhenShutdownFinishesAsync();
    }

    private async Task ReplyWhenShutdownFinishesAsync()
    {
        try
        {
            bool completed = await RequestShutdownAsync().ConfigureAwait(false);
            if (!completed)
                Logger.Warn(
                    $"Encerramento excedeu a graca de {DeliveryGracePeriod.TotalSeconds:0}s; "
                    + "permitindo que o macOS finalize o processo.");
        }
        catch (Exception exception)
        {
            Logger.Error("Falha durante o encerramento; permitindo termino nativo", exception);
        }
        finally
        {
            MainThread.Post(() => _application.ReplyToTermination(terminate: true));
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        if (ReferenceEquals(Interlocked.CompareExchange(ref _current, null, this), this))
            _application.SetDelegate(IntPtr.Zero);
        if (_delegate != IntPtr.Zero) ObjC.SendVoid(_delegate, ObjCSelectors.Release);
        _delegate = IntPtr.Zero;
    }
}
