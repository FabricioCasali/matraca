using System.Drawing;

namespace Matraca;

internal sealed class WindowsTargetWindow : ITargetWindow
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, WindowsTargetDescriptor> _targets = new();
    private WindowsFocusIndicator? _indicator;
    private bool _indicatorEnabled;

    public TargetToken? CaptureActive()
    {
        var handle = TextInjector.GetForegroundWindowHandle();
        if (handle == IntPtr.Zero) return null;

        var token = TargetToken.Create();
        _targets[token.Value] = new WindowsTargetDescriptor(
            handle,
            TextInjector.GetFocusedControl(handle));
        return token;
    }

    public bool IsAlive(TargetToken target)
    {
        if (!TryResolve(target, out var handle)) return false;
        if (TextInjector.IsWindowAlive(handle)) return true;
        _targets.TryRemove(target.Value, out _);
        return false;
    }

    public string GetTitle(TargetToken target)
        => TryResolve(target, out var handle) ? TextInjector.GetWindowTitle(handle) : "";

    public void Release(TargetToken target) => _targets.TryRemove(target.Value, out _);

    public void ConfigureIndicator(bool enabled, string color, int thickness, double opacity)
    {
        _indicatorEnabled = enabled;
        _indicator?.Dispose();
        _indicator = null;
        if (!enabled) return;

        try
        {
            _indicator = new WindowsFocusIndicator(
                ColorTranslator.FromHtml(color),
                thickness,
                (float)opacity);
        }
        catch (Exception exception)
        {
            _indicatorEnabled = false;
            Logger.Warn($"Moldura de foco desativada (cor '{color}' invalida? {exception.Message})");
        }
    }

    public void ShowIndicator(TargetToken? target, string color)
    {
        if (!_indicatorEnabled || _indicator == null) return;
        IntPtr handle = IntPtr.Zero;
        if (target != null && (!TryResolve(target, out handle) || !TextInjector.IsWindowAlive(handle)))
        {
            _indicator.HideBorder();
            return;
        }
        _indicator.SetPinned(handle);
        try { _indicator.SetColor(ColorTranslator.FromHtml(color)); }
        catch { }
        _indicator.ShowBorder();
    }

    public void HideIndicator() => _indicator?.HideBorder();

    internal bool TryResolve(TargetToken target, out IntPtr handle)
    {
        if (_targets.TryGetValue(target.Value, out WindowsTargetDescriptor? descriptor))
        {
            handle = descriptor.Window;
            return true;
        }
        handle = IntPtr.Zero;
        return false;
    }

    internal bool TryResolveDescriptor(TargetToken target, out WindowsTargetDescriptor descriptor)
        => _targets.TryGetValue(target.Value, out descriptor!);

    public void Dispose()
    {
        _indicator?.Dispose();
        _targets.Clear();
    }
}
