using System.Diagnostics.CodeAnalysis;
using Matraca.Core;
using Matraca.Mac.Platform.Interop;
using Matraca.Mac.Platform.Overlay;

namespace Matraca.Mac.Platform;

internal sealed class MacTargetWindow : ITargetWindow
{
    private const int IndicatorRefreshMilliseconds = 100;

    private readonly object _gate = new();
    private readonly Dictionary<Guid, (IntPtr Application, IntPtr Window, int ProcessId)> _targets = [];
    private Timer? _indicatorTimer;
    private MacBorderOverlay? _indicator;
    private MacNotificationObserver? _spaceObserver;
    private MacNotificationObserver? _displayObserver;
    private MacBorderOverlayConfiguration? _baseIndicatorConfiguration;
    private MacBorderOverlayConfiguration? _activeIndicatorConfiguration;
    private TargetToken? _indicatorTarget;
    private bool _indicatorEnabled;
    private bool _indicatorRequested;
    private bool _ownsIndicatorTarget;
    private long _indicatorGeneration;
    private int _indicatorRefreshRunning;
    private bool _disposed;

    public MacTargetWindow()
    {
        Frameworks.EnsureLoaded();
    }

    public TargetToken? CaptureActive()
    {
        if (!TryCaptureActiveHandles(out IntPtr application, out IntPtr window, out int processId))
            return null;
        bool stored = false;

        try
        {
            var token = TargetToken.Create();
            lock (_gate)
            {
                if (_disposed) return null;
                _targets.Add(token.Value, (application, window, processId));
                stored = true;
            }
            return token;
        }
        finally
        {
            if (!stored)
            {
                CoreFoundation.Release(window);
                CoreFoundation.Release(application);
            }
        }
    }

    internal static bool TryCaptureActiveLease([NotNullWhen(true)] out MacTargetLease? lease)
    {
        if (!TryCaptureActiveHandles(out IntPtr application, out IntPtr window, out int processId))
        {
            lease = null;
            return false;
        }

        lease = new MacTargetLease(application, window, processId);
        return true;
    }

    public bool IsAlive(TargetToken target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!TryAcquireLease(target, out MacTargetLease? lease)) return false;

        bool alive;
        using (lease)
        {
            alive = Accessibility.IsTargetAlive(
                lease.Application,
                lease.Window,
                lease.ProcessId);
        }

        if (!alive) Release(target);
        return alive;
    }

    public string GetTitle(TargetToken target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!TryAcquireLease(target, out MacTargetLease? lease)) return string.Empty;

        using (lease)
            return Accessibility.GetStringAttribute(lease.Window, Accessibility.TitleAttribute);
    }

    public void Release(TargetToken target)
    {
        ArgumentNullException.ThrowIfNull(target);
        (IntPtr Application, IntPtr Window, int ProcessId) released;
        bool hideIndicator = false;
        lock (_gate)
        {
            if (!_targets.Remove(target.Value, out released)) return;
            if (_indicatorTarget == target)
            {
                _indicatorTarget = null;
                _indicatorRequested = false;
                _ownsIndicatorTarget = false;
                _indicatorGeneration++;
                _indicatorTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                hideIndicator = true;
            }
        }

        Release(released);
        if (hideIndicator) MainThread.Run(HideOverlay);
    }

    internal bool TryAcquireLease(
        TargetToken target,
        [NotNullWhen(true)] out MacTargetLease? lease)
    {
        ArgumentNullException.ThrowIfNull(target);
        lock (_gate)
        {
            if (_disposed || !_targets.TryGetValue(target.Value, out var targetHandles))
            {
                lease = null;
                return false;
            }

            IntPtr application = CoreFoundation.Retain(targetHandles.Application);
            IntPtr window = IntPtr.Zero;
            try
            {
                window = CoreFoundation.Retain(targetHandles.Window);
                lease = new MacTargetLease(application, window, targetHandles.ProcessId);
                return true;
            }
            catch
            {
                CoreFoundation.Release(window);
                CoreFoundation.Release(application);
                throw;
            }
        }
    }

    public void ConfigureIndicator(bool enabled, string color, int thickness, double opacity)
    {
        MacBorderOverlayConfiguration? configuration = null;
        if (enabled)
        {
            try { configuration = new MacBorderOverlayConfiguration(color, thickness, opacity); }
            catch (Exception exception)
            {
                enabled = false;
                Logger.Warn($"Moldura de foco desativada (configuracao invalida: {exception.Message})");
            }
        }

        TargetToken? ownedTarget = null;
        long generation;
        lock (_gate)
        {
            if (_disposed) return;
            _indicatorEnabled = enabled;
            _baseIndicatorConfiguration = configuration;
            if (configuration != null) _activeIndicatorConfiguration = configuration;
            generation = ++_indicatorGeneration;
            if (!enabled)
            {
                if (_ownsIndicatorTarget) ownedTarget = _indicatorTarget;
                _indicatorTarget = null;
                _indicatorRequested = false;
                _ownsIndicatorTarget = false;
                _indicatorTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            }
        }

        if (ownedTarget != null) Release(ownedTarget);
        MainThread.Run(() => ApplyIndicatorConfiguration(generation, configuration));
    }

    public void ShowIndicator(TargetToken? target, string color)
    {
        TargetToken? effectiveTarget = target;
        bool ownsTarget = false;
        MacBorderOverlayConfiguration? baseConfiguration;
        lock (_gate)
        {
            if (_disposed || !_indicatorEnabled) return;
            baseConfiguration = _baseIndicatorConfiguration;
            if (target == null && _indicatorRequested && _ownsIndicatorTarget)
            {
                effectiveTarget = _indicatorTarget;
                ownsTarget = effectiveTarget != null;
            }
        }

        if (baseConfiguration == null) return;
        if (target == null && effectiveTarget == null)
        {
            effectiveTarget = CaptureActive();
            ownsTarget = effectiveTarget != null;
        }

        MacBorderOverlayConfiguration activeConfiguration;
        try
        {
            activeConfiguration = new MacBorderOverlayConfiguration(
                color,
                baseConfiguration.Thickness,
                baseConfiguration.Opacity);
        }
        catch
        {
            activeConfiguration = baseConfiguration;
        }

        TargetToken? previousOwnedTarget = null;
        bool targetChanged;
        long generation;
        lock (_gate)
        {
            if (_disposed || !_indicatorEnabled)
            {
                if (ownsTarget) previousOwnedTarget = effectiveTarget;
                generation = _indicatorGeneration;
                targetChanged = false;
            }
            else
            {
                targetChanged = _indicatorTarget != effectiveTarget;
                if (targetChanged && _ownsIndicatorTarget)
                    previousOwnedTarget = _indicatorTarget;
                _indicatorTarget = effectiveTarget;
                _ownsIndicatorTarget = ownsTarget;
                _indicatorRequested = effectiveTarget != null;
                _activeIndicatorConfiguration = activeConfiguration;
                generation = ++_indicatorGeneration;
                if (_indicatorRequested)
                {
                    _indicatorTimer ??= new Timer(
                        RefreshIndicator,
                        null,
                        Timeout.Infinite,
                        Timeout.Infinite);
                    _indicatorTimer.Change(0, IndicatorRefreshMilliseconds);
                }
                else
                {
                    _indicatorTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                    targetChanged = true;
                }
            }
        }

        if (previousOwnedTarget != null) Release(previousOwnedTarget);
        MainThread.Run(() => ApplyIndicatorRequest(
            generation,
            activeConfiguration,
            targetChanged));
    }

    public void HideIndicator()
    {
        TargetToken? ownedTarget = null;
        lock (_gate)
        {
            if (_disposed) return;
            if (_ownsIndicatorTarget) ownedTarget = _indicatorTarget;
            _indicatorTarget = null;
            _indicatorRequested = false;
            _ownsIndicatorTarget = false;
            _indicatorGeneration++;
            _indicatorTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        }

        if (ownedTarget != null) Release(ownedTarget);
        MainThread.Run(HideOverlay);
    }

    public void Dispose()
    {
        List<(IntPtr Application, IntPtr Window, int ProcessId)> released;
        Timer? indicatorTimer;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _indicatorEnabled = false;
            _indicatorRequested = false;
            _indicatorTarget = null;
            _ownsIndicatorTarget = false;
            _indicatorGeneration++;
            indicatorTimer = _indicatorTimer;
            _indicatorTimer = null;
            released = [.. _targets.Values];
            _targets.Clear();
        }

        indicatorTimer?.Dispose();
        foreach (var target in released) Release(target);
        MainThread.Run(DisposeOverlay);
    }

    private void RefreshIndicator(object? state)
    {
        if (Interlocked.Exchange(ref _indicatorRefreshRunning, 1) != 0) return;
        try
        {
            TargetToken? target;
            long generation;
            lock (_gate)
            {
                if (_disposed || !_indicatorEnabled || !_indicatorRequested)
                    return;
                target = _indicatorTarget;
                generation = _indicatorGeneration;
            }

            bool alive = false;
            bool visible = false;
            CGRect bounds = default;
            if (target != null && TryAcquireLease(target, out MacTargetLease? lease))
            {
                using (lease)
                {
                    alive = Accessibility.IsTargetAlive(
                        lease.Application,
                        lease.Window,
                        lease.ProcessId);
                    if (alive)
                        visible = MacWindowVisibility.TryGetOnScreenBounds(lease, out bounds);
                }
            }

            if (target != null && !alive) Release(target);
            MainThread.Post(() => ApplyIndicatorBounds(generation, visible, bounds));
        }
        catch (Exception exception)
        {
            Logger.Error("Falha ao atualizar a moldura da janela-alvo", exception);
        }
        finally
        {
            Volatile.Write(ref _indicatorRefreshRunning, 0);
        }
    }

    private void ApplyIndicatorConfiguration(
        long generation,
        MacBorderOverlayConfiguration? configuration)
    {
        MainThread.VerifyAccess();
        lock (_gate)
        {
            if (_disposed || generation != _indicatorGeneration)
                return;
            if (!_indicatorEnabled || configuration == null)
            {
                _indicator?.Hide();
                return;
            }

            _indicator ??= new MacBorderOverlay(configuration);
            EnsureEnvironmentObservers();
            _indicator.Configure(configuration);
        }
    }

    private void ApplyIndicatorRequest(
        long generation,
        MacBorderOverlayConfiguration configuration,
        bool hideBeforeRefresh)
    {
        MainThread.VerifyAccess();
        lock (_gate)
        {
            if (_disposed
                || !_indicatorEnabled
                || generation != _indicatorGeneration)
                return;
            EnsureEnvironmentObservers();
            _indicator ??= new MacBorderOverlay(configuration);
            _indicator.Configure(configuration);
            if (hideBeforeRefresh || !_indicatorRequested) _indicator.Hide();
        }
    }

    private void ApplyIndicatorBounds(long generation, bool visible, CGRect quartzBounds)
    {
        MainThread.VerifyAccess();
        lock (_gate)
        {
            if (_disposed
                || !_indicatorEnabled
                || !_indicatorRequested
                || generation != _indicatorGeneration)
                return;
            EnsureEnvironmentObservers();
            if (!visible
                || _activeIndicatorConfiguration == null
                || !MacScreenCoordinates.TryQuartzToAppKit(quartzBounds, out CGRect appKitBounds))
            {
                _indicator?.Hide();
                return;
            }

            _indicator ??= new MacBorderOverlay(_activeIndicatorConfiguration);
            _indicator.Show(appKitBounds);
        }
    }

    private void HideOverlay()
    {
        MainThread.VerifyAccess();
        _indicator?.Hide();
    }

    private void EnsureEnvironmentObservers()
    {
        MainThread.VerifyAccess();
        if (_spaceObserver == null)
        {
            IntPtr workspace = ObjC.Send(ObjCClasses.NSWorkspace, ObjCSelectors.SharedWorkspace);
            IntPtr center = ObjC.Send(workspace, ObjCSelectors.NotificationCenter);
            _spaceObserver = new MacNotificationObserver(
                center,
                "NSWorkspaceActiveSpaceDidChangeNotification",
                OnEnvironmentChanged);
        }
        _displayObserver ??= new MacNotificationObserver(
            ObjC.Send(ObjCClasses.NSNotificationCenter, ObjCSelectors.DefaultCenter),
            "NSApplicationDidChangeScreenParametersNotification",
            OnEnvironmentChanged);
    }

    private void OnEnvironmentChanged()
    {
        MainThread.VerifyAccess();
        lock (_gate)
        {
            if (_disposed) return;
            _indicatorGeneration++;
            _indicator?.Hide();
            if (_indicatorEnabled && _indicatorRequested)
                _indicatorTimer?.Change(0, IndicatorRefreshMilliseconds);
        }
    }

    private void DisposeOverlay()
    {
        MainThread.VerifyAccess();
        _spaceObserver?.Dispose();
        _spaceObserver = null;
        _displayObserver?.Dispose();
        _displayObserver = null;
        _indicator?.Dispose();
        _indicator = null;
    }

    private static void Release((IntPtr Application, IntPtr Window, int ProcessId) target)
    {
        CoreFoundation.Release(target.Window);
        CoreFoundation.Release(target.Application);
    }

    private static bool TryCaptureActiveHandles(
        out IntPtr application,
        out IntPtr window,
        out int processId)
    {
        IntPtr system = IntPtr.Zero;
        application = IntPtr.Zero;
        window = IntPtr.Zero;
        processId = 0;
        bool captured = false;
        try
        {
            system = Accessibility.CreateSystemWideElement();
            captured = system != IntPtr.Zero
                && Accessibility.TryCopyElementAttribute(
                    system,
                    Accessibility.FocusedApplicationAttribute,
                    out application)
                && Accessibility.TryGetProcessId(application, out processId)
                && processId > 0
                && processId != Environment.ProcessId
                && Accessibility.TryCopyElementAttribute(
                    application,
                    Accessibility.FocusedWindowAttribute,
                    out window)
                && Accessibility.TryGetProcessId(window, out int windowProcessId)
                && windowProcessId == processId
                && string.Equals(
                    Accessibility.GetStringAttribute(application, Accessibility.RoleAttribute),
                    Accessibility.ApplicationRole,
                    StringComparison.Ordinal)
                && string.Equals(
                    Accessibility.GetStringAttribute(window, Accessibility.RoleAttribute),
                    Accessibility.WindowRole,
                    StringComparison.Ordinal);
            return captured;
        }
        finally
        {
            CoreFoundation.Release(system);
            if (!captured)
            {
                CoreFoundation.Release(window);
                CoreFoundation.Release(application);
                window = IntPtr.Zero;
                application = IntPtr.Zero;
                processId = 0;
            }
        }
    }
}
