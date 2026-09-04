using System.Diagnostics;
using System.Text.Json;
using Matraca.Core;
using Matraca.Mac.Platform.Interop;

namespace Matraca.Mac.Platform.Keyboard;

internal static class MacKeyboardCaptureSmoke
{
    public static int Run(string[] args)
    {
        if (args.Length != 2)
        {
            Logger.Error("Uso: --keyboard-capture-smoke <resultado-json>.");
            return 2;
        }

        try
        {
            Frameworks.EnsureLoaded();
            ObjCClasses.Warm();
            using var pool = AutoreleasePool.New();
            MacApplication.Shared().ConfigureAsAccessory();
            MainThread.Initialize();
            using var hook = new MacKeyboardHook(new Matraca.Core.Config
            {
                Hotkey = new HotkeyGesture("F15", KeyMods.None),
            });
            int dictationEvents = 0;
            hook.DictationKeyChanged += (_, _) => Interlocked.Increment(ref dictationEvents);
            hook.Start();
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            Task<HotkeyGesture> captured = hook.CaptureNextAsync(cancellation.Token);
            int completedAfterKeyDown = 0;

            _ = Task.Run(async () =>
            {
                await Task.Delay(100).ConfigureAwait(false);
                PostKey(0x6B, pressed: true);
                await Task.Delay(150).ConfigureAwait(false);
                Volatile.Write(ref completedAfterKeyDown, captured.IsCompleted ? 1 : 0);
                PostKey(0x6B, pressed: false);
            });

            IntPtr runLoop = ObjC.Send(ObjCClasses.NSRunLoop, ObjCSelectors.CurrentRunLoop);
            var elapsed = Stopwatch.StartNew();
            while (!captured.IsCompleted && elapsed.Elapsed < TimeSpan.FromSeconds(3))
            {
                IntPtr until = ObjC.SendDouble(
                    ObjCClasses.NSDate,
                    ObjCSelectors.DateWithTimeIntervalSinceNow,
                    0.02);
                ObjC.SendVoid(runLoop, ObjCSelectors.RunUntilDate, until);
            }

            HotkeyGesture gesture = captured.GetAwaiter().GetResult();
            bool completedEarly = Volatile.Read(ref completedAfterKeyDown) != 0;
            bool success = !completedEarly
                && gesture.ToString() == "F14"
                && dictationEvents == 0;
            File.WriteAllText(args[1], JsonSerializer.Serialize(new
            {
                success,
                completedAfterKeyDown = completedEarly,
                hotkey = gesture.ToString(),
                dictationEvents,
            }));
            return success ? 0 : 1;
        }
        catch (Exception exception)
        {
            Exception root = exception.GetBaseException();
            File.WriteAllText(args[1], JsonSerializer.Serialize(new
            {
                success = false,
                error = root.GetType().Name,
                root.Message,
            }));
            return 1;
        }
    }

    private static void PostKey(ushort keyCode, bool pressed)
    {
        IntPtr source = CoreGraphics.CGEventSourceCreate(CoreGraphics.HidSystemState);
        IntPtr keyEvent = CoreGraphics.CGEventCreateKeyboardEvent(source, keyCode, pressed);
        try { CoreGraphics.CGEventPost(CoreGraphics.HidEventTap, keyEvent); }
        finally
        {
            CoreGraphics.CFRelease(keyEvent);
            CoreGraphics.CFRelease(source);
        }
    }
}
