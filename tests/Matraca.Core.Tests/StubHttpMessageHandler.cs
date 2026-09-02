namespace Matraca.Core.Tests;

internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _send;

    public StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> send)
    {
        _send = send;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_send(request, cancellationToken));
    }
}
