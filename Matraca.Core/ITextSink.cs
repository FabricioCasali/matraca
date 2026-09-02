namespace Matraca.Core;

public interface ITextSink : IDisposable
{
    /// <summary>
    /// Completes only after text, optional Enter, and any clipboard/focus restoration have finished.
    /// DeliveryQueue owns cross-platform serialization; implementations perform one indivisible request.
    /// </summary>
    Task<TextDeliveryResult> DeliverAsync(
        TextDeliveryRequest request,
        CancellationToken cancellationToken = default);
}
