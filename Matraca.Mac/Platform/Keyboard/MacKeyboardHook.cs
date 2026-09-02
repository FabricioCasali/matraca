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
    private const ulong ShiftMask = 1UL << 17;
    private const ulong ControlMask = 1UL << 18;
    private const ulong AlternateMask = 1UL << 19;
    private const ulong CommandMask = 1UL << 20;

    private static MacKeyboardHook? _current;

    private readonly AsyncCallbackQueue<(int Kind, ushort KeyCode, KeyMods Modifiers)> _callbacks;
    private readonly HotkeyGesture? _dictationGesture;
    private readonly HotkeyGesture? _pinGesture;
    private readonly ushort _dictationKeyCode;
    private readonly ushort _pinKeyCode;
    private readonly bool _discover;
    private IntPtr _tap;
    private IntPtr _source;
    private bool _dictationDown;
    private bool _pinDown;
    private volatile bool _suspended;
    private int _disposed;

    public MacKeyboardHook(Matraca.Core.Config config)
    {
        ArgumentNullException.ThrowIfNull(config);
        _discover = config.DiscoverMode;
        _dictationGesture = config.Hotkey;
        _pinGesture = config.PinHotkey;
        _dictationKeyCode = _dictationGesture == null ? (ushort)0 : MacHotkeyTranslator.ToKeyCode(_dictationGesture);
        _pinKeyCode = _pinGesture == null ? (ushort)0 : MacHotkeyTranslator.ToKeyCode(_pinGesture);
        _callbacks = new AsyncCallbackQueue<(int, ushort, KeyMods)>(
            Publish,
            exception => Logger.Error("Falha ao publicar evento do teclado Mac", exception));
    }

    public event Action<HotkeyGesture, bool>? DictationKeyChanged;
    public event Action<HotkeyGesture>? PinToggled;
    public event Action<HotkeyGesture>? KeyDiscovered;

    public bool Suspended
    {
        get => _suspended;
        set => _suspended = value;
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (_tap != IntPtr.Zero) return;
        if (_current != null)
            throw new InvalidOperationException("Only one macOS event tap can run at a time.");

        _current = this;
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
            _callbacks.TryPost((Rearmed, 0, KeyMods.None));
            return @event;
        }

        ushort keyCode = (ushort)CoreGraphics.CGEventGetIntegerValueField(
            @event,
            CoreGraphics.KeyboardEventKeycode);
        bool pressed = type == CoreGraphics.KeyDown;
        if (_suspended)
        {
            if (!pressed && keyCode == _dictationKeyCode) _dictationDown = false;
            if (!pressed && keyCode == _pinKeyCode) _pinDown = false;
            return @event;
        }
        if (pressed && CoreGraphics.CGEventGetIntegerValueField(
                @event,
                CoreGraphics.KeyboardEventAutorepeat) != 0)
            return IsActiveCapturedKey(keyCode) ? IntPtr.Zero : @event;

        KeyMods modifiers = ToModifiers(CoreGraphics.CGEventGetFlags(@event));
        if (_discover && pressed)
        {
            _callbacks.TryPost((Discovery, keyCode, modifiers));
            return IntPtr.Zero;
        }

        if (pressed && Matches(_dictationGesture, _dictationKeyCode, keyCode, modifiers))
        {
            if (!_dictationDown)
            {
                _dictationDown = true;
                _callbacks.TryPost((DictationDown, keyCode, modifiers));
            }
            return IntPtr.Zero;
        }
        if (!pressed && _dictationDown && keyCode == _dictationKeyCode)
        {
            _dictationDown = false;
            _callbacks.TryPost((DictationUp, keyCode, modifiers));
            return IntPtr.Zero;
        }
        if (pressed && Matches(_pinGesture, _pinKeyCode, keyCode, modifiers))
        {
            if (!_pinDown)
            {
                _pinDown = true;
                _callbacks.TryPost((Pin, keyCode, modifiers));
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
            || (_pinDown && keyCode == _pinKeyCode);

    private static bool Matches(
        HotkeyGesture? gesture,
        ushort configuredKeyCode,
        ushort keyCode,
        KeyMods modifiers)
        => gesture != null && configuredKeyCode == keyCode && gesture.Modifiers == modifiers;

    private void Publish((int Kind, ushort KeyCode, KeyMods Modifiers) signal)
    {
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
                KeyDiscovered?.Invoke(new HotkeyGesture(
                    MacHotkeyTranslator.NameForKeyCode(signal.KeyCode),
                    signal.Modifiers));
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
        if (ReferenceEquals(_current, this)) _current = null;
        _callbacks.Dispose();
    }
}
