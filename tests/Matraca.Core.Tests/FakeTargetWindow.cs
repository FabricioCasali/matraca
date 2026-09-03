using Matraca.Core;

namespace Matraca.Core.Tests;

internal sealed class FakeTargetWindow : ITargetWindow
{
    private readonly HashSet<TargetToken> _alive = new();

    public TargetToken? Active { get; set; }
    public string Title { get; set; } = "fake target";
    public List<TargetToken> Released { get; } = new();
    public List<(bool Enabled, string Color, int Thickness, double Opacity)> Configurations { get; } = new();
    public List<(TargetToken? Target, string Color)> Indicators { get; } = new();
    public int HideCount { get; private set; }
    public bool Disposed { get; private set; }

    public TargetToken CreateAliveTarget()
    {
        var target = TargetToken.Create();
        _alive.Add(target);
        return target;
    }

    public void Kill(TargetToken target) => _alive.Remove(target);
    public TargetToken? CaptureActive() => Active;
    public bool IsAlive(TargetToken target) => _alive.Contains(target);
    public string GetTitle(TargetToken target) => IsAlive(target) ? Title : "";

    public void Release(TargetToken target)
    {
        Released.Add(target);
        _alive.Remove(target);
    }

    public void ConfigureIndicator(bool enabled, string color, int thickness, double opacity)
        => Configurations.Add((enabled, color, thickness, opacity));

    public void ShowIndicator(TargetToken? target, string color)
        => Indicators.Add((target, color));

    public void HideIndicator() => HideCount++;
    public void Dispose() => Disposed = true;
}
