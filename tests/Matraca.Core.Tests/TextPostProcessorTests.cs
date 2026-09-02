using Xunit;

namespace Matraca.Core.Tests;

public sealed class TextPostProcessorTests
{
    [Fact]
    public async Task SuccessfulRequestReturnsTrimmedText()
    {
        using var processor = new TextPostProcessor(
            (_, _) => Task.FromResult<string?>("  Corrected text.  "),
            1000);

        Assert.Equal("Corrected text.", await processor.CleanAsync("raw text"));
    }

    [Fact]
    public async Task TimeoutReturnsOriginalTextEvenWhenRequestIgnoresCancellation()
    {
        using var processor = new TextPostProcessor(
            async (_, _) =>
            {
                await Task.Delay(500);
                return "late text";
            },
            10);

        Assert.Equal("raw text", await processor.CleanAsync("raw text"));
    }

    [Fact]
    public async Task RequestErrorAndEmptyResponseReturnOriginalText()
    {
        using var failing = new TextPostProcessor(
            (_, _) => Task.FromException<string?>(new HttpRequestException("offline")),
            1000);
        using var empty = new TextPostProcessor(
            (_, _) => Task.FromResult<string?>("   "),
            1000);

        Assert.Equal("raw text", await failing.CleanAsync("raw text"));
        Assert.Equal("raw text", await empty.CleanAsync("raw text"));
    }
}
