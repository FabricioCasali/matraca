using System.Net;
using System.Text;
using Xunit;

namespace Matraca.Core.Tests;

public sealed class DeepSeekBalanceClientTests
{
    [Fact]
    public async Task GetsOfficialBalanceWithoutSendingAnythingButTheKey()
    {
        HttpMethod? method = null;
        Uri? uri = null;
        string? authorization = null;
        using var http = new HttpClient(new StubHttpMessageHandler((request, _) =>
        {
            method = request.Method;
            uri = request.RequestUri;
            authorization = request.Headers.Authorization?.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {
                      "is_available":true,
                      "balance_infos":[{
                        "currency":"USD",
                        "total_balance":"12.34",
                        "granted_balance":"2.34",
                        "topped_up_balance":"10.00"
                      }]
                    }
                    """, Encoding.UTF8, "application/json"),
            };
        }));

        DeepSeekBalance result = await new DeepSeekBalanceClient(http)
            .GetAsync("secret", CancellationToken.None);

        Assert.Equal(HttpMethod.Get, method);
        Assert.Equal(DeepSeekBalanceClient.Endpoint, uri?.AbsoluteUri);
        Assert.Equal("Bearer secret", authorization);
        Assert.True(result.IsAvailable);
        DeepSeekBalanceInfo balance = Assert.Single(result.Balances);
        Assert.Equal("USD", balance.Currency);
        Assert.Equal(12.34m, balance.TotalBalance);
        Assert.Equal(2.34m, balance.GrantedBalance);
        Assert.Equal(10m, balance.ToppedUpBalance);
    }

    [Fact]
    public async Task MissingKeyIsRejectedBeforeTheRequest()
    {
        using var http = new HttpClient(new StubHttpMessageHandler(
            (_, _) => throw new InvalidOperationException("request should not run")));

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new DeepSeekBalanceClient(http).GetAsync(""));

        Assert.Contains("Configure a chave", exception.Message);
    }
}
