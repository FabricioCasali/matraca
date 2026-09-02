using Matraca.Core;

namespace Matraca.Core.Tests;

internal sealed class FakeTextSink : ITextSink
{
    private readonly TaskCompletionSource<TextDeliveryResult> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TextDeliveryRequest? Request { get; private set; }

    public Task<TextDeliveryResult> DeliverAsync(
        TextDeliveryRequest request,
        CancellationToken cancellationToken = default)
    {
        Request = request;
        return _completion.Task;
    }

    public void Complete(TextDeliveryResult result) => _completion.TrySetResult(result);

    public void Dispose() { }
}
