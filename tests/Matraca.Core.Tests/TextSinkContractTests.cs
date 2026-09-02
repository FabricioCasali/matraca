using Matraca.Core;
using Xunit;

namespace Matraca.Core.Tests;

public sealed class TextSinkContractTests
{
    [Fact]
    public async Task DeliveryCarriesAllInputsAndCompletesOnlyWithTheResult()
    {
        using var sink = new FakeTextSink();
        var target = TargetToken.Create();
        var request = new TextDeliveryRequest(
            "texto",
            true,
            TextDeliveryMethod.TargetWithFocus,
            target);

        var delivery = sink.DeliverAsync(request);

        Assert.False(delivery.IsCompleted);
        Assert.Equal(request, sink.Request);
        sink.Complete(TextDeliveryResult.Delivered);
        Assert.Equal(TextDeliveryResult.Delivered, await delivery);
    }
}
