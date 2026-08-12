using System.Collections.Concurrent;
using Matraca.MacSpike.Interop;
using Matraca.MacSpike.Tap;

namespace Matraca.MacSpike.Text;

/// <summary>
/// Digita texto na janela em foco por CGEvent — o gemeo macOS do
/// <c>TextInjector.PasteText</c> (caminho unicode) do Windows.
///
/// DUAS REGRAS HERDADAS DO WINDOWS, e as duas sao lei aqui:
///
/// 1. FORA DA THREAD DO TAP. Uma thread dedicada, consumidora unica de uma fila — o gemeo
///    exato do TextInjector.Enqueue. Nao e' preferencia de estilo: e' a lei 5. Digitar na
///    thread que hospeda o hook trava o hook, e o sintoma e' o MESMO dos dois lados (texto
///    sem espacos, cortado no meio); a diferenca e' que no Windows o sistema DESCARTA
///    caracteres pelo LowLevelHooksTimeout e no macOS ele DESABILITA o event tap, deixando
///    o app mudo.
///
/// 2. FATIAR. CGEventKeyboardSetUnicodeString tem limite pratico por evento, e rajada grande
///    estoura a fila de terminal e de apps Electron — a mesma classe de bug que fez o lado
///    Windows fatiar em 40 caracteres com pausa de 2 ms.
///
/// E toda entrega leva a MARCA DA FONTE (kCGEventSourceUserData = 0x4D545243, "MTRC"), que e'
/// como o tap reconhece o proprio texto e o deixa passar sem trabalho. Nunca por "e'
/// sintetico": remapeadores tambem injetam.
/// </summary>
internal static unsafe class EventInjector
{
    /// <summary>
    /// Bloco de unidades UTF-16 por evento. O Windows usa 40; aqui 20 por conservadorismo,
    /// ja' que a evidencia empirica do limite pratico do
    /// CGEventKeyboardSetUnicodeString e' mais escassa. Se o texto chegar inteiro com folga,
    /// da' para subir — e' um numero, nao um dogma.
    /// </summary>
    private const int ChunkUnits = 20;

    /// <summary>Folga entre eventos, pelo mesmo motivo do <c>UnicodeChunkPauseMs</c> do Windows.</summary>
    private const int ChunkPauseMs = 2;

    private static readonly BlockingCollection<string> Queue = new(new ConcurrentQueue<string>());
    private static Thread? _worker;
    private static IntPtr _source;

    /// <summary>Sobe a thread consumidora. Idempotente.</summary>
    public static void Start()
    {
        if (_worker != null) return;
        Frameworks.EnsureLoaded();

        // Uma fonte de evento para o processo inteiro (regra do +1: e' liberada no Stop).
        _source = CoreGraphics.CGEventSourceCreate(CoreGraphics.kCGEventSourceStateHIDSystemState);
        if (_source == IntPtr.Zero)
            SpikeLog.Warn("CGEventSourceCreate devolveu NULL; seguindo com fonte nula.");

        _worker = new Thread(Loop) { IsBackground = true, Name = "matraca-inject" };
        _worker.Start();
        SpikeLog.Info($"injetor pronto (blocos de {ChunkUnits} unidades UTF-16, "
                    + $"pausa de {ChunkPauseMs} ms, marca 0x{KeyTap.InjectionTag:X})");
    }

    /// <summary>Enfileira o texto. Volta na hora; quem digita e' a thread do injetor.</summary>
    public static void Enqueue(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        Queue.Add(text);
    }

    /// <summary>Espera a fila esvaziar (so' para o spike poder medir e encerrar).</summary>
    public static void Drain(int timeoutMs = 10000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (Queue.Count > 0 && sw.ElapsedMilliseconds < timeoutMs) Thread.Sleep(10);
        Thread.Sleep(ChunkPauseMs * 4);   // folga p/ o ultimo bloco chegar ao alvo
    }

    private static void Loop()
    {
        // CGEvent e' Core Foundation puro — nada de ObjC aqui, entao nao ha' autorelease
        // pool a criar. Se um dia esta thread tocar ObjC, ela PRECISA de um
        // AutoreleasePool em volta do trabalho.
        foreach (var text in Queue.GetConsumingEnumerable())
        {
            try { Type(text); }
            catch (Exception ex) { SpikeLog.Error("falha ao digitar o texto", ex); }
        }
    }

    private static void Type(string text)
    {
        var t0 = SpikeLog.ElapsedMs;
        int events = 0;

        for (int start = 0; start < text.Length; start += ChunkUnits)
        {
            int len = Math.Min(ChunkUnits, text.Length - start);
            var chunk = text.AsSpan(start, len);

            fixed (char* p = chunk)
            {
                Post((ushort*)p, len, keyDown: true);
                Post((ushort*)p, len, keyDown: false);
            }
            events += 2;

            if (start + len < text.Length) Thread.Sleep(ChunkPauseMs);
        }

        SpikeLog.Info($"digitados {text.Length} caracteres em {events} eventos, "
                    + $"{SpikeLog.ElapsedMs - t0} ms");
    }

    private static void Post(ushort* units, int len, bool keyDown)
    {
        var evt = CoreGraphics.CGEventCreateKeyboardEvent(_source, 0, keyDown);
        if (evt == IntPtr.Zero) { SpikeLog.Warn("CGEventCreateKeyboardEvent devolveu NULL"); return; }

        CoreGraphics.CGEventKeyboardSetUnicodeString(evt, (nuint)len, units);
        // A marca: e' o que faz o nosso proprio tap devolver o evento sem trabalho nenhum.
        CoreGraphics.CGEventSetIntegerValueField(evt, CoreGraphics.kCGEventSourceUserData,
                                                 KeyTap.InjectionTag);
        CoreGraphics.CGEventPost(CoreGraphics.kCGHIDEventTap, evt);

        // Regra do +1: sem este CFRelease, um ditado longo vaza centenas de CGEvent.
        CoreGraphics.CFRelease(evt);
    }

    public static void Stop()
    {
        Queue.CompleteAdding();
        if (_source != IntPtr.Zero) { CoreGraphics.CFRelease(_source); _source = IntPtr.Zero; }
    }
}
