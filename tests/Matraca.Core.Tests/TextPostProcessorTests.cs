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
        const string original = "  raw text\r\n";
        using var processor = new TextPostProcessor(
            async (_, _) =>
            {
                await Task.Delay(500);
                return "late text";
            },
            10);

        Assert.Equal(original, await processor.CleanAsync(original));
    }

    [Fact]
    public async Task RequestErrorAndEmptyResponseReturnOriginalText()
    {
        const string original = "  raw text\r\n";
        using var failing = new TextPostProcessor(
            (_, _) => Task.FromException<string?>(new HttpRequestException("offline")),
            1000);
        using var empty = new TextPostProcessor(
            (_, _) => Task.FromResult<string?>(null),
            1000);

        Assert.Equal(original, await failing.CleanAsync(original));
        Assert.Equal(original, await empty.CleanAsync(original));
    }

    [Fact]
    public async Task CallerCancellationReturnsOriginalWhenRequestIgnoresCancellation()
    {
        const string original = "  raw text\r\n";
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        using var processor = new TextPostProcessor(
            async (_, _) =>
            {
                started.TrySetResult();
                await release.Task;
                return "late text";
            },
            60000);

        Task<string> cleaning = processor.CleanAsync(original, cancellation.Token);
        await started.Task;
        cancellation.Cancel();

        Assert.Equal(original, await cleaning.WaitAsync(TimeSpan.FromSeconds(1)));
        release.TrySetResult();
    }

    [Fact]
    public async Task CallerCancellationBoundsDelegateThatBlocksBeforeReturningTask()
    {
        const string original = "  raw text\r\n";
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        using var processor = new TextPostProcessor(
            (_, _) =>
            {
                started.TrySetResult();
                release.Task.GetAwaiter().GetResult();
                return Task.FromResult<string?>("late text");
            },
            60000);

        Task<string> cleaning = processor.CleanAsync(original, cancellation.Token);
        await started.Task;
        cancellation.Cancel();

        Assert.Equal(original, await cleaning.WaitAsync(TimeSpan.FromSeconds(1)));
        release.TrySetResult();
    }

    [Fact]
    public async Task DisposalCancelsRequestAndReturnsOriginalText()
    {
        const string original = "  raw text\r\n";
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var processor = new TextPostProcessor(
            async (_, _) =>
            {
                started.TrySetResult();
                await release.Task;
                return "late text";
            },
            60000);

        Task<string> cleaning = processor.CleanAsync(original);
        await started.Task;
        processor.Dispose();

        Assert.Equal(original, await cleaning.WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.Equal(original, await processor.CleanAsync(original));
        release.TrySetResult();
    }
}
