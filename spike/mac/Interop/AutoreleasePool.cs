namespace Matraca.MacSpike.Interop;

/// <summary>
/// NSAutoreleasePool com escopo de <c>using</c>.
///
/// Sao dois regimes de memoria neste projeto, e confundi-los vaza:
///  * Core Foundation / CoreGraphics (CGEventCreate*, CGEventSourceCreate,
///    CFMachPortCreateRunLoopSource) devolvem +1 e exigem CFRelease EXPLICITO. Num ditado
///    longo sao centenas de CGEvent: sem CFRelease e' vazamento visivel.
///  * Objective-C (alloc/init, e tudo que e' autoreleased) vive sob pool. A thread
///    principal tem o pool da run loop; QUALQUER thread de fundo que toque ObjC precisa de
///    um pool proprio em volta do trabalho.
///
/// Idealmente a thread de injecao nao toca ObjC (CGEvent e' CF puro), mas quando tocar, e'
/// aqui.
/// </summary>
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
