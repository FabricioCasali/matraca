namespace MacUpdateProbe;

// Synthetic workload ONLY. No microphone, real dictation queue or production references.
internal sealed class InstallGate
{
    private readonly object _sync = new();
    private bool _capturing;
    private bool _pendingDelivery;
    private bool _frozen;

    // ABI shared with ProbeDriver.m. All operations are fail-closed and atomic.
    internal bool Handle(int operation)
    {
        lock (_sync)
        {
            switch (operation)
            {
                case 0: return !_capturing && !_pendingDelivery && !_frozen;
                case 1:
                    if (_frozen || _capturing || _pendingDelivery) return false;
                    _capturing = true;
                    return true;
                case 2:
                    if (!_capturing) return false;
                    _capturing = false;
                    _pendingDelivery = true;
                    return true;
                case 3:
                    if (_capturing || !_pendingDelivery) return false;
                    _pendingDelivery = false;
                    return true;
                case 4:
                    if (_capturing || _pendingDelivery || _frozen) return false;
                    _frozen = true;
                    return true;
                case 5: _frozen = false; return true;
                case 6: return !_capturing && !_pendingDelivery;
                default: return false;
            }
        }
    }
}
