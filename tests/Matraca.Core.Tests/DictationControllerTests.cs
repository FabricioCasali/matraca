using System.Collections.Concurrent;
using Matraca.Core;
using Xunit;

namespace Matraca.Core.Tests;

public sealed class DictationControllerTests
{
    [Theory]
    [InlineData("toggle", false)]
    [InlineData("hold", false)]
    [InlineData("live", true)]
    [InlineData("push", true)]
    public async Task AllFourModesRunTheSharedPipeline(string mode, bool streaming)
    {
        Config config = NewConfig(mode, autoEnter: true);
        var (controller, keyboard, audio, sink, _, shell) = CreateController(config, ["spoken"]);
        controller.Start();

        keyboard.RaiseDictation(true);
        await controller.DrainAsync();
        string recordingText = shell.States.Last(item => item.State == ShellState.Recording).Text;
        Assert.Contains(mode is "hold" or "push" ? "solte" : "aperte de novo", recordingText);
        audio.Emit(0.2f, 10);
        if (mode is "hold" or "push")
        {
            keyboard.RaiseDictation(false);
        }
        else
        {
            keyboard.RaiseDictation(false);
            keyboard.RaiseDictation(true);
        }
        await controller.DrainAsync();

        TextDeliveryRequest[] requests = sink.Requests.ToArray();
        if (streaming)
        {
            Assert.Equal(2, requests.Length);
            Assert.Equal("spoken ", requests[0].Text);
            Assert.False(requests[0].PressEnter);
            Assert.Equal("", requests[1].Text);
            Assert.True(requests[1].PressEnter);
        }
        else
        {
            var request = Assert.Single(requests);
            Assert.Equal("spoken", request.Text);
            Assert.True(request.PressEnter);
        }
        Assert.False(controller.IsSessionActive);
        await controller.ShutdownAsync();
    }

    [Fact]
    public async Task RepeatedKeyDownDoesNotToggleUntilKeyUpArrives()
    {
        var (controller, keyboard, audio, _, _, _) = CreateController(NewConfig("toggle"), ["text"]);
        controller.Start();

        keyboard.RaiseDictation(true);
        keyboard.RaiseDictation(true);
        await controller.DrainAsync();

        Assert.True(controller.IsSessionActive);
        Assert.Equal(1, audio.StartCount);
        Assert.Equal(0, audio.StopCount);

        keyboard.RaiseDictation(false);
        keyboard.RaiseDictation(true);
        await controller.DrainAsync();
        Assert.False(controller.IsSessionActive);
        Assert.Equal(1, audio.StopCount);
        await controller.ShutdownAsync();
    }

    [Fact]
    public async Task ReplacedKeyboardOwnsEventsAfterHotReload()
    {
        var setup = CreateController(NewConfig("toggle"), ["text"]);
        var replacement = new FakeKeyboardHook();
        setup.Controller.Start();

        await setup.Controller.ReplaceKeyboardHookAsync(replacement);
        setup.Keyboard.RaiseDictation(true);
        await setup.Controller.DrainAsync();

        Assert.True(setup.Keyboard.Disposed);
        Assert.True(replacement.Started);
        Assert.False(setup.Controller.IsSessionActive);

        replacement.RaiseDictation(true);
        await setup.Controller.DrainAsync();
        Assert.True(setup.Controller.IsSessionActive);
        await setup.Controller.ShutdownAsync();
    }

    [Fact]
    public async Task KeyboardReplacementWaitsForHoldSessionKeyUp()
    {
        var setup = CreateController(NewConfig("hold"), ["text"]);
        var replacement = new FakeKeyboardHook();
        setup.Controller.Start();
        setup.Keyboard.RaiseDictation(true);
        await setup.Controller.DrainAsync();

        Task replace = setup.Controller.ReplaceKeyboardHookAsync(replacement);
        await setup.Controller.DrainAsync();
        Assert.False(replace.IsCompleted);
        Assert.False(setup.Keyboard.Disposed);

        setup.Keyboard.RaiseDictation(false);
        await setup.Controller.DrainAsync();
        await replace;
        Assert.True(setup.Keyboard.Disposed);
        Assert.True(replacement.Started);
        Assert.False(setup.Controller.IsSessionActive);
        await setup.Controller.ShutdownAsync();
    }

    [Fact]
    public async Task KeyboardReplacementWaitsForToggleKeyUpAfterStop()
    {
        var setup = CreateController(NewConfig("toggle"), ["text"]);
        var replacement = new FakeKeyboardHook();
        setup.Controller.Start();
        setup.Keyboard.RaiseDictation(true);
        setup.Keyboard.RaiseDictation(false);
        await setup.Controller.DrainAsync();
        setup.Keyboard.RaiseDictation(true);
        await setup.Controller.DrainAsync();
        Assert.False(setup.Controller.IsSessionActive);

        Task replace = setup.Controller.ReplaceKeyboardHookAsync(replacement);
        await setup.Controller.DrainAsync();
        Assert.False(replace.IsCompleted);

        setup.Keyboard.RaiseDictation(false);
        await setup.Controller.DrainAsync();
        await replace;
        Assert.True(replacement.Started);
        await setup.Controller.ShutdownAsync();
    }

    [Fact]
    public async Task ConfigAndKeyboardReplacementBecomeVisibleTogetherAfterKeyUp()
    {
        var setup = CreateController(NewConfig("hold"), ["text"]);
        var replacement = new FakeKeyboardHook();
        setup.Controller.Start();
        setup.Keyboard.RaiseDictation(true);
        await setup.Controller.DrainAsync();

        Task apply = setup.Controller.ApplyConfigAsync(NewConfig("toggle"), replacement);
        await setup.Controller.DrainAsync();
        Assert.False(apply.IsCompleted);
        Assert.Equal("hold", setup.Controller.CurrentConfig.Mode);
        Assert.False(replacement.Started);

        setup.Keyboard.RaiseDictation(false);
        await setup.Controller.DrainAsync();
        await apply;
        Assert.Equal("toggle", setup.Controller.CurrentConfig.Mode);
        Assert.True(replacement.Started);
        await setup.Controller.ShutdownAsync();
    }

    [Fact]
    public async Task ShutdownReleasesPendingKeyboardReplacement()
    {
        var setup = CreateController(NewConfig("hold"), ["text"]);
        var replacement = new FakeKeyboardHook();
        setup.Controller.Start();
        setup.Keyboard.RaiseDictation(true);
        await setup.Controller.DrainAsync();
        Task replace = setup.Controller.ReplaceKeyboardHookAsync(replacement);
        await setup.Controller.DrainAsync();

        await setup.Controller.ShutdownAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => replace);
        Assert.True(replacement.Disposed);
    }

    [Fact]
    public async Task KeyUpDuringBusyStopAllowsTheNextToggle()
    {
        var deliveryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDelivery = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sink = new RecordingTextSink(async (_, _, _) =>
        {
            deliveryStarted.TrySetResult();
            await releaseDelivery.Task;
            return TextDeliveryResult.Delivered;
        });
        var setup = CreateController(NewConfig("toggle"), ["first", "second"], sink);
        setup.Controller.Start();
        setup.Keyboard.RaiseDictation(true);
        setup.Keyboard.RaiseDictation(false);
        await setup.Controller.DrainAsync();

        setup.Keyboard.RaiseDictation(true);
        await deliveryStarted.Task;
        setup.Keyboard.RaiseDictation(false);
        releaseDelivery.TrySetResult();
        await setup.Controller.DrainAsync();

        setup.Keyboard.RaiseDictation(true);
        await setup.Controller.DrainAsync();
        Assert.True(setup.Controller.IsSessionActive);
        await setup.Controller.ShutdownAsync();
    }

    [Fact]
    public async Task LiveSegmentsRemainOrderedAndFinalEnterWaitsForTheirDelivery()
    {
        var firstDelivery = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sink = new RecordingTextSink(async (_, call, _) =>
        {
            if (call == 1)
            {
                firstDelivery.TrySetResult();
                await releaseFirst.Task;
            }
            return TextDeliveryResult.Delivered;
        });
        var responses = new ConcurrentQueue<string>(["one", "two"]);
        var setup = CreateController(
            NewConfig("live", autoEnter: true),
            responses,
            sink: sink);
        setup.Controller.Start();
        await setup.Controller.HandleDictationKeyAsync(true);
        EmitPhrase(setup.Audio);
        EmitPhrase(setup.Audio);
        await firstDelivery.Task;

        await setup.Controller.HandleDictationKeyAsync(false);
        Task stop = setup.Controller.HandleDictationKeyAsync(true);
        await Task.Delay(20);
        Assert.False(stop.IsCompleted);
        Assert.Single(sink.Requests);

        releaseFirst.TrySetResult();
        await stop;
        Assert.Equal(["one ", "two ", ""], sink.Requests.Select(request => request.Text));
        Assert.Equal([false, false, true], sink.Requests.Select(request => request.PressEnter));
        await setup.Controller.ShutdownAsync();
    }

    [Fact]
    public async Task EmptyStreamingSessionNeverSendsEnter()
    {
        var (controller, _, audio, sink, _, _) = CreateController(
            NewConfig("push", autoEnter: true),
            ["must not be used"]);
        controller.Start();

        await controller.HandleDictationKeyAsync(true);
        audio.Emit(0f, 20);
        await controller.HandleDictationKeyAsync(false);

        Assert.Empty(sink.Requests);
        await controller.ShutdownAsync();
    }

    [Fact]
    public async Task FailedStreamingChunkDoesNotSendFinalEnter()
    {
        var sink = new RecordingTextSink((_, _, _) =>
            Task.FromResult(TextDeliveryResult.Failed));
        var setup = CreateController(NewConfig("live", autoEnter: true), ["spoken"], sink);
        setup.Controller.Start();

        await setup.Controller.HandleDictationKeyAsync(true);
        EmitPhrase(setup.Audio);
        await setup.Controller.HandleDictationKeyAsync(false);
        await setup.Controller.HandleDictationKeyAsync(true);

        var request = Assert.Single(sink.Requests);
        Assert.Equal("spoken ", request.Text);
        Assert.False(request.PressEnter);
        await setup.Controller.ShutdownAsync();
    }

    [Fact]
    public async Task FailedLaterStreamingChunkDoesNotSubmitPartialText()
    {
        var sink = new RecordingTextSink((_, call, _) => Task.FromResult(
            call == 1 ? TextDeliveryResult.Delivered : TextDeliveryResult.Failed));
        var setup = CreateController(NewConfig("live", autoEnter: true), ["one", "two"], sink);
        setup.Controller.Start();

        await setup.Controller.HandleDictationKeyAsync(true);
        EmitPhrase(setup.Audio);
        EmitPhrase(setup.Audio);
        await setup.Controller.HandleDictationKeyAsync(false);
        await setup.Controller.HandleDictationKeyAsync(true);

        Assert.Equal(["one ", "two "], sink.Requests.Select(request => request.Text));
        Assert.All(sink.Requests, request => Assert.False(request.PressEnter));
        await setup.Controller.ShutdownAsync();
    }

    [Fact]
    public async Task PostProcessingFallbackIsStoredBeforeFailedDelivery()
    {
        string home = NewTemporaryDirectory();
        try
        {
            var history = new DictationHistory(AppPaths.ForMac(home), 10);
            using var processor = new TextPostProcessor(
                (_, _) => Task.FromException<string?>(new HttpRequestException("offline")),
                1000);
            bool historyWasReadyAtSink = false;
            var sink = new RecordingTextSink((_, _, _) =>
            {
                historyWasReadyAtSink = history.Snapshot().Single().Text == "raw";
                return Task.FromResult(TextDeliveryResult.Failed);
            });
            var setup = CreateController(
                NewConfig("hold"),
                ["raw"],
                sink,
                _ => processor,
                _ => history);
            setup.Controller.Start();

            await setup.Controller.HandleDictationKeyAsync(true);
            await setup.Controller.HandleDictationKeyAsync(false);

            Assert.True(historyWasReadyAtSink);
            Assert.Equal("raw", history.Snapshot().Single().Text);
            await setup.Controller.ShutdownAsync();
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }

    [Fact]
    public async Task DeadPinIsReleasedAndDeliveryFallsBackToActiveTarget()
    {
        var setup = CreateController(NewConfig("hold"), ["text"]);
        var pinned = setup.Targets.CreateAliveTarget();
        setup.Targets.Active = pinned;
        setup.Controller.Start();
        await setup.Controller.TogglePinAsync();
        await setup.Controller.HandleDictationKeyAsync(true);
        setup.Targets.Kill(pinned);

        await setup.Controller.HandleDictationKeyAsync(false);

        var request = Assert.Single(setup.Sink.Requests);
        Assert.Null(request.Target);
        Assert.Null(setup.Controller.PinnedTarget);
        Assert.Contains(pinned, setup.Targets.Released);
        await setup.Controller.ShutdownAsync();
    }

    [Fact]
    public async Task FocusPinFailureFallsBackWithoutLosingHistoryOrder()
    {
        var sink = new RecordingTextSink((_, call, _) => Task.FromResult(
            call == 1 ? TextDeliveryResult.Failed : TextDeliveryResult.Delivered));
        var setup = CreateController(NewConfig("hold"), ["text"], sink);
        var pinned = setup.Targets.CreateAliveTarget();
        setup.Targets.Active = pinned;
        setup.Controller.Start();
        await setup.Controller.TogglePinAsync();

        await setup.Controller.HandleDictationKeyAsync(true);
        await setup.Controller.HandleDictationKeyAsync(false);

        TextDeliveryRequest[] requests = sink.Requests.ToArray();
        Assert.Equal(2, requests.Length);
        Assert.Equal(pinned, requests[0].Target);
        Assert.Null(requests[1].Target);
        await setup.Controller.ShutdownAsync();
    }

    [Fact]
    public async Task ConfigAppliedDuringSessionOnlyAffectsTheNextSession()
    {
        Config first = NewConfig("toggle", autoEnter: false, inputDevice: "old mic");
        Config second = NewConfig("toggle", autoEnter: true, inputDevice: "new mic");
        var setup = CreateController(first, ["first", "second"]);
        setup.Controller.Start();

        await setup.Controller.HandleDictationKeyAsync(true);
        await setup.Controller.ApplyConfigAsync(second);
        await setup.Controller.HandleDictationKeyAsync(false);
        await setup.Controller.HandleDictationKeyAsync(true);

        await setup.Controller.HandleDictationKeyAsync(false);
        await setup.Controller.HandleDictationKeyAsync(true);
        await setup.Controller.HandleDictationKeyAsync(false);
        await setup.Controller.HandleDictationKeyAsync(true);

        Assert.Equal(["old mic", "new mic"], setup.Audio.StartedDevices);
        Assert.Equal([false, true], setup.Sink.Requests.Select(request => request.PressEnter));
        await setup.Controller.ShutdownAsync();
    }

    [Fact]
    public async Task ModelConfigBecomesVisibleOnlyAfterAtomicReloadCompletes()
    {
        var reloadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reload = new TaskCompletionSource<TranscriptionModel>(TaskCreationOptions.RunContinuationsAsynchronously);
        Config first = NewConfig("toggle", modelPath: "old-model");
        Config second = NewConfig("hold", modelPath: "new-model");
        var setup = CreateController(first, [], modelFactory: (config, _) =>
        {
            if (config.ModelPath == "old-model")
                return Task.FromResult(new TranscriptionModel((_, _) => Task.FromResult("old")));
            reloadStarted.SetResult();
            return reload.Task;
        });
        setup.Controller.Start();
        await setup.Controller.DrainAsync();

        Task apply = setup.Controller.ApplyConfigAsync(second);
        await reloadStarted.Task;
        Assert.False(apply.IsCompleted);
        Assert.Equal("old-model", setup.Controller.CurrentConfig.ModelPath);
        Assert.Equal("toggle", setup.Controller.CurrentConfig.Mode);

        reload.SetResult(new TranscriptionModel((_, _) => Task.FromResult("new")));
        await apply;
        Assert.Equal("new-model", setup.Controller.CurrentConfig.ModelPath);
        Assert.Equal("hold", setup.Controller.CurrentConfig.Mode);
        await setup.Controller.ShutdownAsync();
    }

    [Fact]
    public async Task ShutdownCancelsAnActiveTranscription()
    {
        var transcriptionStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var setup = CreateController(NewConfig("hold"), [], modelFactory: (_, _) =>
            Task.FromResult(new TranscriptionModel(async (_, cancellationToken) =>
            {
                transcriptionStarted.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return "never";
            })));
        setup.Controller.Start();
        await setup.Controller.HandleDictationKeyAsync(true);
        Task stop = setup.Controller.HandleDictationKeyAsync(false);
        await transcriptionStarted.Task;

        Task shutdown = setup.Controller.ShutdownAsync();

        await Task.WhenAll(stop, shutdown).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Empty(setup.Sink.Requests);
    }

    [Fact]
    public async Task ModelReloadDuringSessionCompletesOnlyAfterTheNewModelIsReady()
    {
        var reload = new TaskCompletionSource<TranscriptionModel>(TaskCreationOptions.RunContinuationsAsynchronously);
        Config first = NewConfig("hold", modelPath: "old-model");
        Config second = NewConfig("hold", modelPath: "new-model");
        var setup = CreateController(first, [], modelFactory: (config, _) =>
            config.ModelPath == "old-model"
                ? Task.FromResult(new TranscriptionModel((_, _) => Task.FromResult("old")))
                : reload.Task);
        setup.Controller.Start();
        await setup.Controller.HandleDictationKeyAsync(true);

        Task apply = setup.Controller.ApplyConfigAsync(second);
        await setup.Controller.DrainAsync();
        Assert.False(apply.IsCompleted);
        Assert.Equal("old-model", setup.Controller.CurrentConfig.ModelPath);

        Task stop = setup.Controller.HandleDictationKeyAsync(false);
        reload.SetResult(new TranscriptionModel((_, _) => Task.FromResult("new")));
        await Task.WhenAll(stop, apply);
        Assert.Equal("new-model", setup.Controller.CurrentConfig.ModelPath);
        await setup.Controller.ShutdownAsync();
    }

    [Fact]
    public async Task FailedModelReloadDuringSessionPreservesThePreviousConfig()
    {
        Config first = NewConfig("hold", modelPath: "old-model");
        Config second = NewConfig("hold", modelPath: "broken-model");
        var setup = CreateController(first, [], modelFactory: (config, _) =>
            config.ModelPath == "old-model"
                ? Task.FromResult(new TranscriptionModel((_, _) => Task.FromResult("old")))
                : Task.FromException<TranscriptionModel>(new IOException("broken")));
        setup.Controller.Start();
        await setup.Controller.HandleDictationKeyAsync(true);
        Task apply = setup.Controller.ApplyConfigAsync(second);
        await setup.Controller.DrainAsync();

        await setup.Controller.HandleDictationKeyAsync(false);

        await Assert.ThrowsAsync<InvalidOperationException>(() => apply);
        Assert.Equal("old-model", setup.Controller.CurrentConfig.ModelPath);
        await setup.Controller.ShutdownAsync();
    }

    private static void EmitPhrase(FakeAudioCapture audio)
    {
        audio.Emit(0.2f, 10);
        audio.Emit(0f, 16);
    }

    private static (DictationController Controller, FakeKeyboardHook Keyboard, FakeAudioCapture Audio, RecordingTextSink Sink, FakeTargetWindow Targets, FakeShell Shell) CreateController(
        Config config,
        IEnumerable<string> responses,
        RecordingTextSink? sink = null,
        Func<Config, TextPostProcessor?>? postProcessorFactory = null,
        Func<Config, DictationHistory?>? historyFactory = null,
        Func<Config, CancellationToken, Task<TranscriptionModel>>? modelFactory = null)
        => CreateController(config, new ConcurrentQueue<string>(responses), sink,
            postProcessorFactory, historyFactory, modelFactory);

    private static (DictationController Controller, FakeKeyboardHook Keyboard, FakeAudioCapture Audio, RecordingTextSink Sink, FakeTargetWindow Targets, FakeShell Shell) CreateController(
        Config config,
        ConcurrentQueue<string> responses,
        RecordingTextSink? sink = null,
        Func<Config, TextPostProcessor?>? postProcessorFactory = null,
        Func<Config, DictationHistory?>? historyFactory = null,
        Func<Config, CancellationToken, Task<TranscriptionModel>>? modelFactory = null)
    {
        var keyboard = new FakeKeyboardHook();
        var audio = new FakeAudioCapture();
        sink ??= new RecordingTextSink();
        var targets = new FakeTargetWindow();
        var shell = new FakeShell();
        var manager = new TranscriptionModelManager(
            config,
            modelFactory ?? ((_, _) => Task.FromResult(new TranscriptionModel(
                (_, _) => Task.FromResult(responses.TryDequeue(out string? text) ? text : "")))),
            startIdleTimer: false);
        var controller = new DictationController(
            config,
            keyboard,
            audio,
            sink,
            targets,
            shell,
            manager,
            postProcessorFactory,
            historyFactory);
        return (controller, keyboard, audio, sink, targets, shell);
    }

    private static Config NewConfig(
        string mode,
        bool autoEnter = false,
        string inputDevice = "",
        string modelPath = "fake-model") => new()
    {
        ModelPath = modelPath,
        Mode = mode,
        AutoEnter = autoEnter,
        InputDevice = inputDevice,
        Beep = false,
        IdleUnloadMinutes = 0,
    };

    private static string NewTemporaryDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "matraca-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
