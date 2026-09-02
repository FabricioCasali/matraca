using Matraca.Core;

namespace Matraca.Mac.Platform;

internal sealed class MacTargetWindow : ITargetWindow
{
    public TargetToken? CaptureActive() => null;

    public bool IsAlive(TargetToken target) => false;

    public string GetTitle(TargetToken target) => "";

    public void Release(TargetToken target)
    {
    }

    public void ConfigureIndicator(bool enabled, string color, int thickness, double opacity)
    {
    }

    public void ShowIndicator(TargetToken? target, string color)
    {
    }

    public void HideIndicator()
    {
    }

    public void Dispose()
    {
    }
}
