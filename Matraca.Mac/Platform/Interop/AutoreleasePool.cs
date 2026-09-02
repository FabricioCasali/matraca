namespace Matraca.Mac.Platform.Interop;

internal readonly struct AutoreleasePool : IDisposable
{
    private readonly IntPtr _pool;

    private AutoreleasePool(IntPtr pool) => _pool = pool;

    public static AutoreleasePool New()
        => new(ObjC.New(ObjCClasses.NSAutoreleasePool));

    public void Dispose()
    {
        if (_pool != IntPtr.Zero) ObjC.SendVoid(_pool, ObjCSelectors.Drain);
    }
}
