using Matraca.Core;
using Xunit;

namespace Matraca.Core.Tests;

public sealed class TranscriptionModelManagerTests
{
    [Fact]
    public async Task ConcurrentPreloadsShareOneLoad()
    {
        int loads = 0;
        var created = new TaskCompletionSource<TranscriptionModel>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var manager = new TranscriptionModelManager(
            NewConfig("one"),
            (_, _) => { loads++; return created.Task; },
            startIdleTimer: false);

        var first = manager.PreloadAsync();
        var second = manager.PreloadAsync();

        Assert.Equal(1, loads);
        Assert.Equal(TranscriptionModelState.Loading, manager.State);
        created.SetResult(Model("ready"));
        Assert.Same(await first, await second);
        Assert.Equal(TranscriptionModelState.Ready, manager.State);
    }

    [Fact]
    public async Task FailedLoadCanBeRetried()
    {
        int loads = 0;
        using var manager = new TranscriptionModelManager(
            NewConfig("one"),
            (_, _) => ++loads == 1
                ? Task.FromException<TranscriptionModel>(new IOException("broken"))
                : Task.FromResult(Model("retry")),
            startIdleTimer: false);

        Assert.Null(await manager.GetModelAsync());
        Assert.Equal(TranscriptionModelState.Failed, manager.State);
        var model = await manager.GetModelAsync();

        Assert.NotNull(model);
        Assert.Equal("retry", await model.TranscribeAsync([]));
        Assert.Equal(2, loads);
    }

    [Fact]
    public async Task StaleLoadCannotReplaceANewerReload()
    {
        var oldLoad = new TaskCompletionSource<TranscriptionModel>(TaskCreationOptions.RunContinuationsAsynchronously);
        var newLoad = new TaskCompletionSource<TranscriptionModel>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var manager = new TranscriptionModelManager(
            NewConfig("old"),
            (config, _) => config.ModelPath == "old" ? oldLoad.Task : newLoad.Task,
            startIdleTimer: false);

        Task<TranscriptionModel?> stale = manager.PreloadAsync();
        Task<TranscriptionModel?> current = manager.ReloadAsync(NewConfig("new"));
        newLoad.SetResult(Model("new"));
        Assert.Equal("new", await (await current)!.TranscribeAsync([]));

        oldLoad.SetResult(Model("old"));
        Assert.Null(await stale);
        Assert.Equal("new", await (await manager.GetModelAsync())!.TranscribeAsync([]));
    }

    [Fact]
    public async Task FailedAtomicReloadKeepsTheReadyModel()
    {
        using var manager = new TranscriptionModelManager(
            NewConfig("old"),
            (config, _) => config.ModelPath == "old"
                ? Task.FromResult(Model("old"))
                : Task.FromException<TranscriptionModel>(new IOException("new failed")),
            startIdleTimer: false);
        await manager.PreloadAsync();

        Assert.Null(await manager.ReloadAsync(NewConfig("new")));

        Assert.True(manager.IsLoaded);
        Assert.Equal(TranscriptionModelState.Ready, manager.State);
        Assert.NotNull(manager.LastError);
        Assert.Equal("old", await (await manager.GetModelAsync())!.TranscribeAsync([]));

        manager.Unload();
        Assert.Equal("old", await (await manager.GetModelAsync())!.TranscribeAsync([]));
    }

    [Fact]
    public async Task IdleUnloadWaitsForActiveUseAndDisposesTheModel()
    {
        long now = 0;
        int disposals = 0;
        using var manager = new TranscriptionModelManager(
            NewConfig("one", idleMinutes: 1),
            (_, _) => Task.FromResult(new TranscriptionModel(
                (_, _) => Task.FromResult("text"),
                () => disposals++)),
            () => now,
            startIdleTimer: false);
        await manager.PreloadAsync();
        manager.BeginUse();
        now = 61_000;

        Assert.False(manager.UnloadIfIdle());
        manager.EndUse();
        now = 122_000;
        Assert.True(manager.UnloadIfIdle());

        Assert.Equal(1, disposals);
        Assert.Equal(TranscriptionModelState.Unloaded, manager.State);
    }

    [Fact]
    public async Task ExplicitUnloadInvalidatesAnInFlightLoad()
    {
        var pending = new TaskCompletionSource<TranscriptionModel>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var manager = new TranscriptionModelManager(
            NewConfig("one"),
            (_, _) => pending.Task,
            startIdleTimer: false);

        Task<TranscriptionModel?> stale = manager.PreloadAsync();
        manager.Unload();

        Assert.False(manager.IsLoading);
        pending.SetResult(Model("stale"));
        Assert.Null(await stale);
        Assert.False(manager.IsLoaded);
        Assert.Equal(TranscriptionModelState.Unloaded, manager.State);
    }

    [Fact]
    public async Task ConsecutiveTranscriptionsKeepTheLoadedModelResident()
    {
        int loads = 0;
        int transcriptions = 0;
        int disposals = 0;
        var manager = new TranscriptionModelManager(
            NewConfig("one"),
            (_, _) =>
            {
                loads++;
                return Task.FromResult(new TranscriptionModel(
                    (_, _) => Task.FromResult($"text-{++transcriptions}"),
                    () => disposals++));
            },
            startIdleTimer: false);

        var first = await manager.PreloadAsync();
        Assert.Equal("text-1", await first!.TranscribeAsync([]));
        var second = await manager.GetModelAsync();
        Assert.Same(first, second);
        Assert.Equal("text-2", await second!.TranscribeAsync([]));
        Assert.Equal(1, loads);
        Assert.Equal(0, disposals);

        manager.Dispose();
        Assert.Equal(1, disposals);
    }

    [Fact]
    public async Task AtomicReloadRetiresTheOldModelUntilItsUseEnds()
    {
        int oldDisposals = 0;
        int newDisposals = 0;
        using var manager = new TranscriptionModelManager(
            NewConfig("old"),
            (config, _) => Task.FromResult(new TranscriptionModel(
                (_, _) => Task.FromResult(config.ModelPath),
                config.ModelPath == "old" ? () => oldDisposals++ : () => newDisposals++)),
            startIdleTimer: false);
        var old = await manager.PreloadAsync();
        manager.BeginUse();

        var current = await manager.ReloadAsync(NewConfig("new"));

        Assert.Equal("old", await old!.TranscribeAsync([]));
        Assert.Equal("new", await current!.TranscribeAsync([]));
        Assert.Equal(0, oldDisposals);
        manager.EndUse();
        Assert.Equal(1, oldDisposals);
        Assert.Equal(0, newDisposals);
    }

    private static Config NewConfig(string modelPath, int idleMinutes = 0) => new()
    {
        ModelPath = modelPath,
        IdleUnloadMinutes = idleMinutes,
    };

    private static TranscriptionModel Model(string text)
        => new((_, _) => Task.FromResult(text));
}
