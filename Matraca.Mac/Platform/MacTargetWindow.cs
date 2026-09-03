using System.Diagnostics.CodeAnalysis;
using Matraca.Core;
using Matraca.Mac.Platform.Interop;

namespace Matraca.Mac.Platform;

internal sealed class MacTargetWindow : ITargetWindow
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, (IntPtr Application, IntPtr Window, int ProcessId)> _targets = [];
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
        lock (_gate)
        {
            if (!_targets.Remove(target.Value, out released)) return;
        }

        Release(released);
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
    }

    public void ShowIndicator(TargetToken? target, string color)
    {
    }

    public void HideIndicator()
    {
    }

    public void Dispose()
    {
        List<(IntPtr Application, IntPtr Window, int ProcessId)> released;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            released = [.. _targets.Values];
            _targets.Clear();
        }

        foreach (var target in released) Release(target);
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
