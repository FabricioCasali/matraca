namespace Matraca.Core;

public interface ITextSink : IDisposable
{
    /// <summary>
    /// Completes only after text, optional Enter, and any clipboard/focus restoration have finished.
    /// Implementations serialize deliveries so live chunks cannot overtake the final Enter.
    /// </summary>
    Task<TextDeliveryResult> DeliverAsync(
        TextDeliveryRequest request,
        CancellationToken cancellationToken = default);
}
