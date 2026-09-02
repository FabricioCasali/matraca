using Matraca.Core;

namespace Matraca.Core.Tests;

internal sealed class FakeKeyboardHook : IKeyboardHook
{
    public event Action<HotkeyGesture, bool>? DictationKeyChanged;
    public event Action<HotkeyGesture>? PinToggled;
    public event Action<HotkeyGesture>? KeyDiscovered;

    public bool Suspended { get; set; }
    public bool Started { get; private set; }
    public bool Disposed { get; private set; }

    public void Start() => Started = true;

    public void RaiseDictation(bool pressed)
        => DictationKeyChanged?.Invoke(new HotkeyGesture("F15", KeyMods.None), pressed);

    public void RaisePin()
        => PinToggled?.Invoke(new HotkeyGesture("F16", KeyMods.None));

    public void RaiseDiscovered(HotkeyGesture gesture) => KeyDiscovered?.Invoke(gesture);

    public void Dispose() => Disposed = true;
}
