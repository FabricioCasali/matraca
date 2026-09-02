namespace Matraca.Core;

public interface IKeyboardHook : IDisposable
{
    /// <summary>Public callbacks are always dispatched outside the native hook/tap callback.</summary>
    event Action<HotkeyGesture, bool>? DictationKeyChanged;
    event Action<HotkeyGesture>? PinToggled;
    event Action<HotkeyGesture>? KeyDiscovered;

    bool Suspended { get; set; }

    void Start();
}
