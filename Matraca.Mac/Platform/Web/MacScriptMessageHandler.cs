using System.Runtime.InteropServices;
using System.Text.Json;
using Matraca.Core;
using Matraca.Mac.Platform.Interop;

namespace Matraca.Mac.Platform.Web;

internal sealed unsafe class MacScriptMessageHandler : IDisposable
{
    public const string Name = "matraca";
    private static readonly object CallbackGate = new();
    private static readonly Dictionary<IntPtr, MacScriptMessageHandler> Instances = [];
    private static readonly IntPtr HandlerClass = CreateHandlerClass();
    private IntPtr _native;

    public MacScriptMessageHandler()
    {
        MainThread.VerifyAccess();
        _native = ObjC.New(HandlerClass);
        if (_native == IntPtr.Zero)
            throw new InvalidOperationException("Could not create the WKScriptMessageHandler.");
        lock (CallbackGate) Instances.Add(_native, this);
    }

    public IntPtr Handle => _native;
    public event Action<string>? MessageReceived;

    public void Dispose()
    {
        MainThread.VerifyAccess();
        if (_native == IntPtr.Zero) return;

        lock (CallbackGate) Instances.Remove(_native);
        MessageReceived = null;
        ObjC.SendVoid(_native, ObjCSelectors.Release);
        _native = IntPtr.Zero;
    }

    private static IntPtr CreateHandlerClass()
        => ObjCClassBuilder
            .Create("MatracaScriptMessageHandler", ObjCClasses.NSObject)
            .AddMethod(
                ObjCSelectors.DidReceiveScriptMessage,
                (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, void>)&Receive,
                "v@:@@")
            .Register();

    [UnmanagedCallersOnly]
    private static void Receive(
        IntPtr self,
        IntPtr command,
        IntPtr userContentController,
        IntPtr message)
    {
        try
        {
            MacScriptMessageHandler? handler;
            lock (CallbackGate) Instances.TryGetValue(self, out handler);
            if (handler == null) return;

            IntPtr body = ObjC.Send(message, ObjCSelectors.Body);
            if (body == IntPtr.Zero
                || !ObjC.SendBool(body, ObjCSelectors.IsKindOfClass, ObjCClasses.NSString))
            {
                Logger.Warn("WebKit descartou postMessage que nao era string JSON.");
                return;
            }

            string? json = NSStringRef.To(body);
            if (string.IsNullOrWhiteSpace(json)) return;
            using (JsonDocument.Parse(json)) { }
            handler.MessageReceived?.Invoke(json);
        }
        catch (JsonException exception)
        {
            try { Logger.Error("WebKit descartou postMessage com JSON invalido", exception); }
            catch { }
        }
        catch (Exception exception)
        {
            try { Logger.Error("Falha na ponte WebKit para C#", exception); }
            catch { }
        }
    }
}
