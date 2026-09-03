namespace Matraca.Core;

public enum TextDeliveryResult
{
    /// <summary>The complete request was delivered.</summary>
    Delivered,

    /// <summary>The target was unavailable and no injection was attempted.</summary>
    TargetUnavailable,

    /// <summary>The requested delivery method is unsupported and no injection was attempted.</summary>
    Unsupported,

    /// <summary>Delivery failed; injection may have started, so automatic retry is unsafe.</summary>
    Failed,

    /// <summary>Delivery was cancelled; injection may have started, so automatic retry is unsafe.</summary>
    Cancelled,

    /// <summary>The request violated the sink contract and must not be retried automatically.</summary>
    InvalidRequest,
}
