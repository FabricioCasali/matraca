using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Matraca.MacSpike.Interop;

/// <summary>
/// Fila de trabalho da thread principal. E' o unico caminho legal para tocar NSWindow,
/// WKWebView ou NSStatusItem a partir de outra thread.
///
/// O CONTRATO DE THREADS DO PROJETO, do qual a Fase 2 inteira depende:
///  * a thread principal roda [NSApp run] (ou CFRunLoopRun) e nunca retorna;
///  * o callback do event tap roda na run loop onde o tap foi anexado — a principal —
///    portanto ele DECIDE E ENFILEIRA, nada mais (lei 5);
///  * a injecao de texto roda numa thread propria, consumidora unica de uma fila;
///  * o callback do AudioQueue roda numa thread interna do AudioToolbox: so' copia bytes;
///  * voltar para a thread principal e' aqui.
///
/// COMO, E POR QUE ASSIM: em vez de construir um bloco Objective-C a partir de C# (a ABI de
/// bloco e' uma struct com isa/flags/invoke/descriptor — feio e fragil), registramos uma
/// classe <c>MatracaDispatcher</c> com um metodo <c>-pump</c> e chamamos
/// <c>performSelectorOnMainThread:withObject:waitUntilDone:</c>. O pump drena uma
/// ConcurrentQueue gerenciada. Zero blocos, zero dispatch_async, e a maquina de registrar
/// classe ja' era necessaria de qualquer forma.
///
/// (Plano B equivalente, caso isto atrapalhe um dia: um CFRunLoopSource customizado com
/// CFRunLoopSourceSignal + CFRunLoopWakeUp — 100% C, callback direto por
/// [UnmanagedCallersOnly], sem ObjC nenhum.)
/// </summary>
internal static unsafe class MainThread
{
    private static readonly ConcurrentQueue<Action> Queue = new();
    private static IntPtr _dispatcher;
    private static int _mainThreadId;

    /// <summary>Chamar UMA vez, DA thread principal, antes de entrar na run loop.</summary>
    public static void Init()
    {
        if (_dispatcher != IntPtr.Zero) return;

        _mainThreadId = Environment.CurrentManagedThreadId;

        var cls = ObjCClassBuilder
            .Create("MatracaDispatcher", ObjCClasses.NSObject)
            .AddMethod(ObjCSelectors.Pump, (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, void>)&Pump, "v@:")
            .Register();

        _dispatcher = ObjC.New(cls);
        SpikeLog.Info($"MainThread pronta (thread gerenciada {_mainThreadId})");
    }

    public static bool IsMain => Environment.CurrentManagedThreadId == _mainThreadId;

    /// <summary>Enfileira o trabalho para rodar na thread principal, sem esperar.</summary>
    public static void Post(Action work)
    {
        Queue.Enqueue(work);
        if (_dispatcher == IntPtr.Zero)
        {
            SpikeLog.Warn("MainThread.Post antes de MainThread.Init — o trabalho fica na fila.");
            return;
        }
        ObjC.SendPerformOnMain(_dispatcher, ObjCSelectors.PerformOnMainThread,
                               ObjCSelectors.Pump, IntPtr.Zero, waitUntilDone: false);
    }

    /// <summary>
    /// O -pump, chamado pelo runtime do ObjC na thread principal.
    /// try/catch cobrindo tudo: excecao gerenciada atravessando a fronteira nativa derruba
    /// o processo sem stack trace util.
    /// </summary>
    [UnmanagedCallersOnly]
    private static void Pump(IntPtr self, IntPtr cmd)
    {
        try
        {
            while (Queue.TryDequeue(out var work))
            {
                try { work(); }
                catch (Exception ex) { SpikeLog.Error("trabalho da thread principal estourou", ex); }
            }
        }
        catch (Exception ex)
        {
            try { SpikeLog.Error("pump estourou", ex); } catch { }
        }
    }
}
