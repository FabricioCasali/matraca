namespace Matraca.Core;

public interface ITargetWindow : IDisposable
{
    /// <summary>Captures the active platform window without exposing its native handle.</summary>
    TargetToken? CaptureActive();
    bool IsAlive(TargetToken target);
    string GetTitle(TargetToken target);
    void Release(TargetToken target);
    void ConfigureIndicator(bool enabled, string color, int thickness, double opacity);
    void ShowIndicator(TargetToken? target, string color);
    void HideIndicator();
}
