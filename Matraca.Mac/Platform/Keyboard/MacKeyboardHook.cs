using System.Runtime.InteropServices;
using Matraca.Core;
using Matraca.Mac.Platform.Interop;

namespace Matraca.Mac.Platform.Keyboard;

internal sealed unsafe class MacKeyboardHook : IKeyboardHook
{
    private const int DictationDown = 1;
    private const int DictationUp = 2;
    private const int Pin = 3;
    private const int Discovery = 4;
    private const int Rearmed = 5;
    private const int Capture = 6;
    private const ulong ShiftMask = 1UL << 17;
    private const ulong ControlMask = 1UL << 18;
    private const ulong AlternateMask = 1UL << 19;
    private const ulong CommandMask = 1UL << 20;

    private static MacKeyboardHook? _current;

    private readonly object _lifecycleGate = new();
    private readonly object _captureGate = new();
    private readonly AsyncCallbackQueue<(int Kind, ushort KeyCode, KeyMods Modifiers, int Generation)> _callbacks;
    private readonly HotkeyGesture? _dictationGesture;
    private readonly HotkeyGesture? _pinGesture;
    private readonly ushort _dictationKeyCode;
    private readonly ushort _pinKeyCode;
    private readonly bool _discover;
    private IntPtr _tap;
    private IntPtr _source;
    private bool _dictationDown;
    private bool _pinDown;
    private TaskCompletionSource<HotkeyGesture>? _capture;
    private int _captureNext;
    private int _captureGeneration;
    private int _capturedKeyCode = -1;
    private int _capturedGeneration;
    private KeyMods _capturedModifiers;
    private int _suspended;
    private int _disposed;

    public MacKeyboardHook(Matraca.Core.Config config)
    {
        ArgumentNullException.ThrowIfNull(config);
        _discover = config.DiscoverMode;
        _dictationGesture = config.Hotkey;
        _pinGesture = config.PinHotkey;
        _dictationKeyCode = _dictationGesture == null ? (ushort)0 : MacHotkeyTranslator.ToKeyCode(_dictationGesture);
        _pinKeyCode = _pinGesture == null ? (ushort)0 : MacHotkeyTranslator.ToKeyCode(_pinGesture);
        _callbacks = new AsyncCallbackQueue<(int, ushort, KeyMods, int)>(
            Publish,
            exception => Logger.Error("Falha ao publicar evento do teclado Mac", exception));
    }

    public event Action<HotkeyGesture, bool>? DictationKeyChanged;
    public event Action<HotkeyGesture>? PinToggled;
    public event Action<HotkeyGesture>? KeyDiscovered;

    public bool Suspended
    {
        get => Volatile.Read(ref _suspended) != 0;
        set
        {
            if (value) Suspend();
            else Resume();
        }
    }

    public void Start()
    {
        lock (_lifecycleGate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            ClaimCurrent();
            if (Suspended || _tap != IntPtr.Zero) return;
            CreateTap();
        }
    }

    internal Task<HotkeyGesture> CaptureNextAsync(CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<HotkeyGesture>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_captureGate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (_capture != null)
                throw new InvalidOperationException("Ja existe uma captura de tecla em andamento.");
            _capture = completion;
            Interlocked.Increment(ref _captureGeneration);
            Volatile.Write(ref _captureNext, 1);
        }

        cancellationToken.Register(
            static state =>
            {
                var capture = ((MacKeyboardHook Owner, TaskCompletionSource<HotkeyGesture> Completion))state!;
                capture.Owner.CancelCapture(capture.Completion);
            },
            (this, completion));
        return completion.Task;
    }

    private void CompleteCapture(HotkeyGesture gesture, int generation)
    {
        TaskCompletionSource<HotkeyGesture>? completion;
        lock (_captureGate)
        {
            if (generation != Volatile.Read(ref _captureGeneration)) return;
            completion = _capture;
            _capture = null;
            Volatile.Write(ref _captureNext, 0);
            Volatile.Write(ref _capturedGeneration, 0);
        }
        if (HotkeyParser.TryParse(gesture.ToString(), out HotkeyGesture canonical))
            completion?.TrySetResult(canonical);
        else
            completion?.TrySetException(new InvalidOperationException(
                "Use uma tecla de funcao ou uma combinacao com Ctrl, Alt, Shift ou Command."));
    }

    private void CancelCapture(TaskCompletionSource<HotkeyGesture>? expected = null)
    {
        TaskCompletionSource<HotkeyGesture>? completion;
        lock (_captureGate)
        {
            if (expected != null && !ReferenceEquals(_capture, expected)) return;
            completion = _capture;
            _capture = null;
            Volatile.Write(ref _captureNext, 0);
            Volatile.Write(ref _capturedGeneration, 0);
        }
        completion?.TrySetCanceled();
    }

    private void CreateTap()
    {
        ClaimCurrent();

        _tap = CoreGraphics.CGEventTapCreate(
            CoreGraphics.SessionEventTap,
            CoreGraphics.HeadInsertEventTap,
            CoreGraphics.DefaultEventTap,
            CoreGraphics.KeyDownUpMask,
            &Callback,
            IntPtr.Zero);
        if (_tap == IntPtr.Zero)
        {
            _current = null;
            throw new InvalidOperationException("CGEventTapCreate failed; Accessibility permission is required.");
        }

        _source = CoreGraphics.CFMachPortCreateRunLoopSource(IntPtr.Zero, _tap, 0);
        if (_source == IntPtr.Zero)
        {
            CoreGraphics.CFRelease(_tap);
            _tap = IntPtr.Zero;
            _current = null;
            throw new InvalidOperationException("CFMachPortCreateRunLoopSource failed.");
        }
        CoreGraphics.CFRunLoopAddSource(
            CoreGraphics.CFRunLoopGetMain(),
            _source,
            CoreGraphics.CommonModes);
        CoreGraphics.CGEventTapEnable(_tap, true);
        Logger.Info("Event tap do Mac ativo.");
    }

    private void ClaimCurrent()
    {
        MacKeyboardHook? current = Volatile.Read(ref _current);
        if (current != null && !ReferenceEquals(current, this))
            throw new InvalidOperationException("Only one macOS event tap can run at a time.");
        _current = this;
    }

    private void Suspend()
    {
        lock (_lifecycleGate)
        {
            if (Volatile.Read(ref _disposed) != 0
                || Interlocked.Exchange(ref _suspended, 1) != 0)
                return;
            CancelCapture();
            Volatile.Write(ref _capturedKeyCode, -1);
            Volatile.Write(ref _capturedGeneration, 0);
            _dictationDown = false;
            _pinDown = false;
            DestroyTap();
            Logger.Info("Event tap do Mac suspenso para repouso.");
        }
    }

    private void Resume()
    {
        lock (_lifecycleGate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (Volatile.Read(ref _suspended) == 0) return;
            CreateTap();
            Volatile.Write(ref _suspended, 0);
            Logger.Info("Event tap do Mac recriado apos repouso.");
        }
    }

    [UnmanagedCallersOnly]
    private static IntPtr Callback(IntPtr proxy, uint type, IntPtr @event, IntPtr userInfo)
    {
        try { return _current?.Process(type, @event) ?? @event; }
        catch (Exception exception)
        {
            ThreadPool.UnsafeQueueUserWorkItem(
                static error => Logger.Error("Callback do event tap falhou", error),
                exception,
                preferLocal: false);
            return @event;
        }
    }

    private IntPtr Process(uint type, IntPtr @event)
    {
        if (CoreGraphics.CGEventGetIntegerValueField(@event, CoreGraphics.EventSourceUserData)
            == MacInput.InjectionTag)
            return @event;

        if (type is CoreGraphics.TapDisabledByTimeout or CoreGraphics.TapDisabledByUserInput)
        {
            CoreGraphics.CGEventTapEnable(_tap, true);
            _callbacks.TryPost((Rearmed, 0, KeyMods.None, 0));
            return @event;
        }

        ushort keyCode = (ushort)CoreGraphics.CGEventGetIntegerValueField(
            @event,
            CoreGraphics.KeyboardEventKeycode);
        bool pressed = type == CoreGraphics.KeyDown;
        if (Suspended)
        {
            if (!pressed && keyCode == _dictationKeyCode) _dictationDown = false;
            if (!pressed && keyCode == _pinKeyCode) _pinDown = false;
            return @event;
        }
        if (!pressed && Interlocked.CompareExchange(ref _capturedKeyCode, -1, keyCode) == keyCode)
        {
            int generation = Interlocked.Exchange(ref _capturedGeneration, 0);
            if (generation != 0
                && !_callbacks.TryPost((Capture, keyCode, _capturedModifiers, generation)))
                CancelCapture();
            return IntPtr.Zero;
        }
        if (pressed && CoreGraphics.CGEventGetIntegerValueField(
                @event,
                CoreGraphics.KeyboardEventAutorepeat) != 0)
            return IsActiveCapturedKey(keyCode) ? IntPtr.Zero : @event;

        KeyMods modifiers = ToModifiers(CoreGraphics.CGEventGetFlags(@event));
        if (pressed && Interlocked.Exchange(ref _captureNext, 0) != 0)
        {
            Volatile.Write(ref _capturedKeyCode, keyCode);
            int generation = Volatile.Read(ref _captureGeneration);
            _capturedModifiers = modifiers;
            Volatile.Write(ref _capturedGeneration, generation);
            return IntPtr.Zero;
        }
        if (_discover && pressed)
        {
            Volatile.Write(ref _capturedKeyCode, keyCode);
            _callbacks.TryPost((Discovery, keyCode, modifiers, 0));
            return IntPtr.Zero;
        }

        if (pressed && Matches(_dictationGesture, _dictationKeyCode, keyCode, modifiers))
        {
            if (!_dictationDown)
            {
                _dictationDown = true;
                _callbacks.TryPost((DictationDown, keyCode, modifiers, 0));
            }
            return IntPtr.Zero;
        }
        if (!pressed && _dictationDown && keyCode == _dictationKeyCode)
        {
            _dictationDown = false;
            _callbacks.TryPost((DictationUp, keyCode, modifiers, 0));
            return IntPtr.Zero;
        }
        if (pressed && Matches(_pinGesture, _pinKeyCode, keyCode, modifiers))
        {
            if (!_pinDown)
            {
                _pinDown = true;
                _callbacks.TryPost((Pin, keyCode, modifiers, 0));
            }
            return IntPtr.Zero;
        }
        if (!pressed && _pinDown && keyCode == _pinKeyCode)
        {
            _pinDown = false;
            return IntPtr.Zero;
        }
        return @event;
    }

    private bool IsActiveCapturedKey(ushort keyCode)
        => (_dictationDown && keyCode == _dictationKeyCode)
            || (_pinDown && keyCode == _pinKeyCode)
            || Volatile.Read(ref _capturedKeyCode) == keyCode;

    private static bool Matches(
        HotkeyGesture? gesture,
        ushort configuredKeyCode,
        ushort keyCode,
        KeyMods modifiers)
        => gesture != null && configuredKeyCode == keyCode && gesture.Modifiers == modifiers;

    private void Publish((int Kind, ushort KeyCode, KeyMods Modifiers, int Generation) signal)
    {
        if (Suspended)
        {
            if (signal.Kind == Capture && signal.Generation == Volatile.Read(ref _captureGeneration))
                CancelCapture();
            return;
        }
        switch (signal.Kind)
        {
            case DictationDown when _dictationGesture != null:
                DictationKeyChanged?.Invoke(_dictationGesture, true);
                break;
            case DictationUp when _dictationGesture != null:
                DictationKeyChanged?.Invoke(_dictationGesture, false);
                break;
            case Pin when _pinGesture != null:
                PinToggled?.Invoke(_pinGesture);
                break;
            case Discovery:
                var gesture = new HotkeyGesture(
                    MacHotkeyTranslator.NameForKeyCode(signal.KeyCode),
                    signal.Modifiers);
                KeyDiscovered?.Invoke(gesture);
                break;
            case Capture:
                CompleteCapture(new HotkeyGesture(
                    MacHotkeyTranslator.NameForKeyCode(signal.KeyCode),
                    signal.Modifiers), signal.Generation);
                break;
            case Rearmed:
                Logger.Warn("Event tap foi desabilitado pelo sistema e foi religado.");
                break;
        }
    }

    private static KeyMods ToModifiers(ulong flags)
    {
        KeyMods result = KeyMods.None;
        if ((flags & ControlMask) != 0) result |= KeyMods.Ctrl;
        if ((flags & AlternateMask) != 0) result |= KeyMods.Alt;
        if ((flags & ShiftMask) != 0) result |= KeyMods.Shift;
        if ((flags & CommandMask) != 0) result |= KeyMods.Win;
        return result;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        CancelCapture();
        Volatile.Write(ref _suspended, 1);
        lock (_lifecycleGate) DestroyTap();
        if (ReferenceEquals(_current, this)) _current = null;
        _callbacks.Dispose();
    }

    private void DestroyTap()
    {
        if (_tap != IntPtr.Zero)
        {
            CoreGraphics.CGEventTapEnable(_tap, false);
            if (_source != IntPtr.Zero)
            {
                CoreGraphics.CFRunLoopRemoveSource(
                    CoreGraphics.CFRunLoopGetMain(),
                    _source,
                    CoreGraphics.CommonModes);
                CoreGraphics.CFRelease(_source);
                _source = IntPtr.Zero;
            }
            CoreGraphics.CFRelease(_tap);
            _tap = IntPtr.Zero;
        }
    }
}
