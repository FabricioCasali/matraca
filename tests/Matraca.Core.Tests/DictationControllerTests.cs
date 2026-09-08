using System.Collections.Concurrent;
using Matraca.Core;
using Xunit;

namespace Matraca.Core.Tests;

public sealed class DictationControllerTests
{
    [Theory]
    [InlineData("requested", "real", 0.03f, false)]
    [InlineData("requested", "real", 0.005f, true)]
    [InlineData("", "real", 0.03f, false)]
    [InlineData("requested", null, 0.03f, true)]
    [InlineData("requested", "unknown", 0.03f, true)]
    public async Task LiveUsesActualCaptureDeviceNotRequestedOrLegacyThreshold(
        string requested, string? actual, float threshold, bool delivers)
    {
        var config = new Config
        {
            ModelPath = "fake-model", Mode = "live", Beep = false, InputDevice = requested,
            VadThreshold = 0.5f,
            MicSensitivity = new(StringComparer.OrdinalIgnoreCase)
            {
                ["requested"] = 0.5f, ["real"] = threshold,
            },
        };
        var setup = CreateController(config, ["spoken"]);
        setup.Audio.CurrentDevice = actual;
        setup.Controller.Start();
        await setup.Controller.HandleDictationKeyAsync(true);
        setup.Audio.Emit(0.02f, 10);
        await setup.Controller.HandleDictationKeyAsync(false);
        await setup.Controller.HandleDictationKeyAsync(true);
        Assert.Equal(delivers ? 1 : 0, setup.Sink.Requests.Count);
        await setup.Controller.ShutdownAsync();
    }

    [Fact]
    public async Task ReasoningChangeAloneRecreatesReviewer()
    {
        var created = new List<string>();
        Config ConfigWithReasoning(string reasoning) => new()
        {
            ModelPath = "fake-model", Mode = "toggle", Beep = false,
            PostProcessReasoning = reasoning,
        };
        var setup = CreateController(ConfigWithReasoning("off"), [], postProcessorFactory: config =>
        {
            created.Add(config.PostProcessReasoning);
            return null;
        });
        await setup.Controller.ApplyConfigAsync(ConfigWithReasoning("high"));
        Assert.Equal(new[] { "off", "high" }, created);
        await setup.Controller.ApplyConfigAsync(ConfigWithReasoning("high"));
        Assert.Equal(2, created.Count);
        await setup.Controller.ShutdownAsync();
    }

    [Fact]
    public async Task RealMicrophoneAdjustmentIsRestoredAndIncludesStartupFrames()
    {
        var config = new Config
        {
            ModelPath = "fake-model", Mode = "live", Beep = false,
            MicSensitivity = new(StringComparer.OrdinalIgnoreCase) { ["quiet"] = 0.03f, ["sensitive"] = 0.005f },
        };
        var setup = CreateController(config, ["first", "second"]);
        setup.Audio.StartupFrameValue = 0.02f;
        setup.Controller.Start();
        foreach (string device in new[] { "quiet", "sensitive", "quiet" })
        {
            setup.Audio.CurrentDevice = device;
            await setup.Controller.HandleDictationKeyAsync(true);
            await setup.Controller.HandleDictationKeyAsync(false);
            await setup.Controller.HandleDictationKeyAsync(true);
            await setup.Controller.HandleDictationKeyAsync(false);
        }
        Assert.Single(setup.Sink.Requests);
        Assert.Equal(0.03f, config.MicSensitivity["quiet"]);
        await setup.Controller.ShutdownAsync();
    }

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
    public async Task NonStreamingDeliveryPublishesExplicitWritingState()
    {
        var setup = CreateController(NewConfig("hold"), ["spoken"]);
        setup.Controller.Start();

        await setup.Controller.HandleDictationKeyAsync(true);
        await setup.Controller.HandleDictationKeyAsync(false);

        Assert.Contains(setup.Shell.States, state => state.State == ShellState.Writing);
        Assert.DoesNotContain(
            setup.Shell.States,
            state => state.State == ShellState.Busy && state.Text.Contains("escrevendo"));
        await setup.Controller.ShutdownAsync();
    }

    [Fact]
    public async Task ControllerUsesEffectiveUiLanguageForNotifications()
    {
        var setup = CreateController(NewConfig("hold", uiLanguage: UiLanguageResolver.EnglishUnitedStates), []);
        TargetToken target = setup.Targets.CreateAliveTarget();
        setup.Targets.Active = target;
        setup.Targets.Title = "My private editor";
        setup.Controller.Start();

        await setup.Controller.TogglePinAsync();

        Assert.Contains(
            setup.Shell.Notifications,
            notification => notification.Title == "Destination pinned"
                && notification.Message.Contains("My private editor"));
        await setup.Controller.ShutdownAsync();
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
    public async Task UiToggleDeliversToTheExplicitTargetInsteadOfThePanel()
    {
        var setup = CreateController(NewConfig("toggle"), ["spoken"]);
        TargetToken target = setup.Targets.CreateAliveTarget();
        setup.Controller.Start();

        await setup.Controller.ToggleDictationFromUiAsync(target);
        Assert.True(setup.Controller.IsSessionActive);
        await setup.Controller.ToggleDictationFromUiAsync(target);

        TextDeliveryRequest request = Assert.Single(setup.Sink.Requests);
        Assert.Equal(TextDeliveryMethod.TargetWithFocus, request.Method);
        Assert.Same(target, request.Target);
        await setup.Controller.ShutdownAsync();
    }

    [Fact]
    public async Task UiToggleRejectsModesWithDifferentPressSemantics()
    {
        var setup = CreateController(NewConfig("live"), ["spoken"]);
        setup.Controller.Start();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => setup.Controller.ToggleDictationFromUiAsync(null));

        Assert.False(setup.Controller.IsSessionActive);
        await setup.Controller.ShutdownAsync();
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
    public async Task StoppingLiveStillConsumesFramesDrainedByCapture()
    {
        var setup = CreateController(NewConfig("live"), ["last captured phrase"]);
        setup.Controller.Start();
        await setup.Controller.HandleDictationKeyAsync(true);
        setup.Audio.DrainFramesOnStop = () => setup.Audio.Emit(0.2f, 10);
        await setup.Controller.HandleDictationKeyAsync(false);
        await setup.Controller.HandleDictationKeyAsync(true);
        Assert.Equal("last captured phrase ", Assert.Single(setup.Sink.Requests).Text);
        await setup.Controller.ShutdownAsync();
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

    [Theory]
    [InlineData("unicode", TextDeliveryMethod.Unicode)]
    [InlineData("clipboard", TextDeliveryMethod.Clipboard)]
    public async Task UnpinnedDeliveryUsesConfiguredActiveWindowMethod(
        string pasteMethod,
        TextDeliveryMethod expectedMethod)
    {
        var setup = CreateController(NewConfig("hold", pasteMethod: pasteMethod), ["text"]);
        setup.Controller.Start();

        await setup.Controller.HandleDictationKeyAsync(true);
        await setup.Controller.HandleDictationKeyAsync(false);

        var request = Assert.Single(setup.Sink.Requests);
        Assert.Equal(expectedMethod, request.Method);
        Assert.Null(request.Target);
        await setup.Controller.ShutdownAsync();
    }

    [Theory]
    [InlineData("focus", TextDeliveryMethod.TargetWithFocus)]
    [InlineData("nofocus", TextDeliveryMethod.TargetWithoutFocus)]
    public async Task PinnedDeliveryUsesConfiguredTargetMethod(
        string pinDelivery,
        TextDeliveryMethod expectedMethod)
    {
        var setup = CreateController(NewConfig("hold", pinDelivery: pinDelivery), ["text"]);
        var pinned = setup.Targets.CreateAliveTarget();
        setup.Targets.Active = pinned;
        setup.Controller.Start();
        await setup.Controller.TogglePinAsync();

        await setup.Controller.HandleDictationKeyAsync(true);
        await setup.Controller.HandleDictationKeyAsync(false);

        var request = Assert.Single(setup.Sink.Requests);
        Assert.Equal(expectedMethod, request.Method);
        Assert.Equal(pinned, request.Target);
        await setup.Controller.ShutdownAsync();
    }

    [Fact]
    public async Task UnsupportedNoFocusRetriesSamePinnedTargetWithFocus()
    {
        var sink = new RecordingTextSink((request, _, _) => Task.FromResult(
            request.Method == TextDeliveryMethod.TargetWithoutFocus
                ? TextDeliveryResult.Unsupported
                : TextDeliveryResult.Delivered));
        var setup = CreateController(
            NewConfig("hold", pinDelivery: "nofocus", pasteMethod: "clipboard"),
            ["text"],
            sink);
        var pinned = setup.Targets.CreateAliveTarget();
        setup.Targets.Active = pinned;
        setup.Controller.Start();
        await setup.Controller.TogglePinAsync();

        await setup.Controller.HandleDictationKeyAsync(true);
        await setup.Controller.HandleDictationKeyAsync(false);

        TextDeliveryRequest[] requests = sink.Requests.ToArray();
        Assert.Equal(2, requests.Length);
        Assert.Equal(TextDeliveryMethod.TargetWithoutFocus, requests[0].Method);
        Assert.Equal(TextDeliveryMethod.TargetWithFocus, requests[1].Method);
        Assert.All(requests, request => Assert.Equal(pinned, request.Target));
        Assert.Equal(pinned, setup.Controller.PinnedTarget);
        Assert.Empty(setup.Targets.Released);
        await setup.Controller.ShutdownAsync();
    }

    [Fact]
    public async Task UnavailableFocusedRetryFallsBackOnlyAfterNoFocusCapabilityCheck()
    {
        var sink = new RecordingTextSink((_, call, _) => Task.FromResult(call switch
        {
            1 => TextDeliveryResult.Unsupported,
            2 => TextDeliveryResult.TargetUnavailable,
            _ => TextDeliveryResult.Delivered,
        }));
        var setup = CreateController(
            NewConfig("hold", pinDelivery: "nofocus", pasteMethod: "clipboard"),
            ["text"],
            sink);
        var pinned = setup.Targets.CreateAliveTarget();
        setup.Targets.Active = pinned;
        setup.Controller.Start();
        await setup.Controller.TogglePinAsync();

        await setup.Controller.HandleDictationKeyAsync(true);
        await setup.Controller.HandleDictationKeyAsync(false);

        TextDeliveryRequest[] requests = sink.Requests.ToArray();
        Assert.Equal(3, requests.Length);
        Assert.Equal(TextDeliveryMethod.TargetWithoutFocus, requests[0].Method);
        Assert.Equal(TextDeliveryMethod.TargetWithFocus, requests[1].Method);
        Assert.Equal(TextDeliveryMethod.Clipboard, requests[2].Method);
        Assert.Equal(pinned, requests[0].Target);
        Assert.Equal(pinned, requests[1].Target);
        Assert.Null(requests[2].Target);
        Assert.Null(setup.Controller.PinnedTarget);
        Assert.Equal([pinned], setup.Targets.Released);
        await setup.Controller.ShutdownAsync();
    }

    [Fact]
    public async Task UnavailablePinnedTargetFallsBackToConfiguredActiveWindowMethod()
    {
        var sink = new RecordingTextSink((_, call, _) => Task.FromResult(
            call == 1 ? TextDeliveryResult.TargetUnavailable : TextDeliveryResult.Delivered));
        var setup = CreateController(NewConfig("hold", pasteMethod: "clipboard"), ["text"], sink);
        var pinned = setup.Targets.CreateAliveTarget();
        setup.Targets.Active = pinned;
        setup.Controller.Start();
        await setup.Controller.TogglePinAsync();

        await setup.Controller.HandleDictationKeyAsync(true);
        await setup.Controller.HandleDictationKeyAsync(false);

        TextDeliveryRequest[] requests = sink.Requests.ToArray();
        Assert.Equal(2, requests.Length);
        Assert.Equal(TextDeliveryMethod.TargetWithFocus, requests[0].Method);
        Assert.Equal(pinned, requests[0].Target);
        Assert.Equal(TextDeliveryMethod.Clipboard, requests[1].Method);
        Assert.Null(requests[1].Target);
        Assert.Null(setup.Controller.PinnedTarget);
        Assert.Equal([pinned], setup.Targets.Released);
        await setup.Controller.ShutdownAsync();
    }

    [Theory]
    [InlineData("focus", TextDeliveryMethod.TargetWithFocus, TextDeliveryResult.Failed)]
    [InlineData("focus", TextDeliveryMethod.TargetWithFocus, TextDeliveryResult.Cancelled)]
    [InlineData("focus", TextDeliveryMethod.TargetWithFocus, TextDeliveryResult.InvalidRequest)]
    [InlineData("focus", TextDeliveryMethod.TargetWithFocus, TextDeliveryResult.Unsupported)]
    [InlineData("nofocus", TextDeliveryMethod.TargetWithoutFocus, TextDeliveryResult.Failed)]
    [InlineData("nofocus", TextDeliveryMethod.TargetWithoutFocus, TextDeliveryResult.Cancelled)]
    [InlineData("nofocus", TextDeliveryMethod.TargetWithoutFocus, TextDeliveryResult.InvalidRequest)]
    public async Task PinnedDeliveryDoesNotRetryUnsafeOrNonUnavailableResult(
        string pinDelivery,
        TextDeliveryMethod expectedMethod,
        TextDeliveryResult result)
    {
        var sink = new RecordingTextSink((_, _, _) => Task.FromResult(result));
        var setup = CreateController(
            NewConfig("hold", pinDelivery: pinDelivery, pasteMethod: "clipboard"),
            ["text"],
            sink);
        var pinned = setup.Targets.CreateAliveTarget();
        setup.Targets.Active = pinned;
        setup.Controller.Start();
        await setup.Controller.TogglePinAsync();

        await setup.Controller.HandleDictationKeyAsync(true);
        await setup.Controller.HandleDictationKeyAsync(false);

        var request = Assert.Single(sink.Requests);
        Assert.Equal(expectedMethod, request.Method);
        Assert.Equal(pinned, request.Target);
        Assert.Equal(pinned, setup.Controller.PinnedTarget);
        Assert.Empty(setup.Targets.Released);
        await setup.Controller.ShutdownAsync();
    }

    [Fact]
    public async Task PinTitleUnpinAndShutdownReleaseEachCapturedTargetOnce()
    {
        var setup = CreateController(NewConfig("hold"), []);
        var first = setup.Targets.CreateAliveTarget();
        setup.Targets.Active = first;
        setup.Targets.Title = "First editor";
        setup.Controller.Start();

        await setup.Controller.TogglePinAsync();
        Assert.Equal(first, setup.Controller.PinnedTarget);
        Assert.Contains(setup.Shell.States, state => state.Text.Contains("First editor"));
        Assert.Equal((first, setup.Controller.CurrentConfig.FocusBorderColorPinned), setup.Targets.Indicators.Last());

        await setup.Controller.TogglePinAsync();
        Assert.Null(setup.Controller.PinnedTarget);
        Assert.Equal([first], setup.Targets.Released);
        Assert.True(setup.Targets.HideCount >= 2);

        var second = setup.Targets.CreateAliveTarget();
        setup.Targets.Active = second;
        setup.Targets.Title = "Second editor";
        await setup.Controller.TogglePinAsync();
        await setup.Controller.ShutdownAsync();

        Assert.Equal([first, second], setup.Targets.Released);
        Assert.True(setup.Targets.Disposed);
    }

    [Fact]
    public async Task UnpinDefersReleaseUntilStreamingTargetDeliveryCompletes()
    {
        var deliveryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDelivery = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sink = new RecordingTextSink(async (_, _, _) =>
        {
            deliveryStarted.TrySetResult();
            await releaseDelivery.Task;
            return TextDeliveryResult.Delivered;
        });
        var setup = CreateController(NewConfig("push"), ["text"], sink);
        var pinned = setup.Targets.CreateAliveTarget();
        setup.Targets.Active = pinned;
        setup.Controller.Start();
        await setup.Controller.TogglePinAsync();
        await setup.Controller.HandleDictationKeyAsync(true);
        EmitPhrase(setup.Audio);
        await deliveryStarted.Task;

        await setup.Controller.TogglePinAsync();

        Assert.Null(setup.Controller.PinnedTarget);
        Assert.Empty(setup.Targets.Released);

        releaseDelivery.TrySetResult();
        await setup.Controller.HandleDictationKeyAsync(false);

        Assert.Equal([pinned], setup.Targets.Released);
        await setup.Controller.ShutdownAsync();
    }

    [Fact]
    public async Task IndicatorUsesConfiguredValuesAndTracksIdleRecordingBusyAndReload()
    {
        var deliveryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDelivery = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sink = new RecordingTextSink(async (_, _, _) =>
        {
            deliveryStarted.TrySetResult();
            await releaseDelivery.Task;
            return TextDeliveryResult.Delivered;
        });
        Config initial = NewConfig(
            "hold",
            focusBorder: true,
            focusColor: "#111111",
            busyColor: "#222222",
            thickness: 7,
            opacity: 0.6f);
        var setup = CreateController(initial, ["text"], sink);
        setup.Controller.Start();

        var initialConfiguration = Assert.Single(setup.Targets.Configurations);
        Assert.True(initialConfiguration.Enabled);
        Assert.Equal("#111111", initialConfiguration.Color);
        Assert.Equal(7, initialConfiguration.Thickness);
        Assert.Equal(0.6, initialConfiguration.Opacity, precision: 5);
        int initialHideCount = setup.Targets.HideCount;
        Assert.True(initialHideCount >= 1);

        await setup.Controller.HandleDictationKeyAsync(true);
        Assert.Equal((null, "#111111"), setup.Targets.Indicators.Last());

        Task stop = setup.Controller.HandleDictationKeyAsync(false);
        await deliveryStarted.Task;
        Assert.Equal((null, "#222222"), setup.Targets.Indicators.Last());
        releaseDelivery.TrySetResult();
        await stop;
        int completedHideCount = setup.Targets.HideCount;
        Assert.True(completedHideCount > initialHideCount);

        Config updated = NewConfig(
            "hold",
            focusBorder: false,
            focusColor: "#333333",
            busyColor: "#444444",
            thickness: 3,
            opacity: 0.4f);
        await setup.Controller.ApplyConfigAsync(updated);

        var updatedConfiguration = setup.Targets.Configurations.Last();
        Assert.False(updatedConfiguration.Enabled);
        Assert.Equal("#333333", updatedConfiguration.Color);
        Assert.Equal(3, updatedConfiguration.Thickness);
        Assert.Equal(0.4, updatedConfiguration.Opacity, precision: 5);
        Assert.True(setup.Targets.HideCount > completedHideCount);
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
    public async Task ChangingPostProcessProviderOrEndpointRecreatesProcessor()
    {
        var configurations = new List<(string Provider, string Endpoint)>();
        var setup = CreateController(
            NewConfig("toggle", postProcessProvider: "anthropic"),
            [],
            postProcessorFactory: config =>
            {
                configurations.Add((config.PostProcessProvider, config.PostProcessEndpoint));
                return new TextPostProcessor((text, _) => Task.FromResult<string?>(text), 1000);
            });

        await setup.Controller.ApplyConfigAsync(NewConfig(
            "toggle",
            postProcessProvider: "openai-compatible",
            postProcessEndpoint: "http://localhost:11434/v1/chat/completions"));

        Assert.Equal(
            [
                ("anthropic", ""),
                ("openai-compatible", "http://localhost:11434/v1/chat/completions"),
            ],
            configurations);
        await setup.Controller.ShutdownAsync();
    }

    [Fact]
    public async Task HistoryLimitHotReloadKeepsEntriesFromTheActiveSession()
    {
        string home = NewTemporaryDirectory();
        try
        {
            var paths = AppPaths.ForMac(home);
            int factoryCalls = 0;
            var setup = CreateController(
                NewConfig("hold", historyMaxItems: 10),
                ["first", "second"],
                historyFactory: config =>
                {
                    factoryCalls++;
                    return new DictationHistory(paths, config.HistoryMaxItems);
                });
            setup.Controller.Start();
            await setup.Controller.HandleDictationKeyAsync(true);

            await setup.Controller.ApplyConfigAsync(NewConfig("hold", historyMaxItems: 2));
            await setup.Controller.HandleDictationKeyAsync(false);
            await setup.Controller.HandleDictationKeyAsync(true);
            await setup.Controller.HandleDictationKeyAsync(false);

            Assert.Equal(1, factoryCalls);
            Assert.Equal(["second", "first"],
                new DictationHistory(paths, 10).Snapshot().Select(entry => entry.Text));
            await setup.Controller.ShutdownAsync();
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }

    [Fact]
    public async Task HistoryEnablementHotReloadAppliesAfterTheActiveSession()
    {
        string home = NewTemporaryDirectory();
        try
        {
            var paths = AppPaths.ForMac(home);
            var setup = CreateController(
                NewConfig("hold", history: true),
                ["first", "not stored", "third"],
                historyFactory: config => config.History
                    ? new DictationHistory(paths, config.HistoryMaxItems)
                    : null);
            setup.Controller.Start();
            await setup.Controller.HandleDictationKeyAsync(true);

            await setup.Controller.ApplyConfigAsync(NewConfig("hold", history: false));
            await setup.Controller.HandleDictationKeyAsync(false);
            await setup.Controller.HandleDictationKeyAsync(true);
            await setup.Controller.HandleDictationKeyAsync(false);

            Assert.Equal(["first"],
                new DictationHistory(paths, 10).Snapshot().Select(entry => entry.Text));

            await setup.Controller.ApplyConfigAsync(NewConfig("hold", history: true));
            await setup.Controller.HandleDictationKeyAsync(true);
            await setup.Controller.HandleDictationKeyAsync(false);

            Assert.Equal(["third", "first"],
                new DictationHistory(paths, 10).Snapshot().Select(entry => entry.Text));
            await setup.Controller.ShutdownAsync();
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
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
    public async Task ShutdownCancelsPostProcessingWithoutStartingDelivery()
    {
        string home = NewTemporaryDirectory();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var paths = AppPaths.ForMac(home);
            var history = new DictationHistory(paths, 10);
            var postProcessingStarted = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var processor = new TextPostProcessor(
                async (_, _) =>
                {
                    postProcessingStarted.TrySetResult();
                    await release.Task;
                    return "late text";
                },
                60000);
            var setup = CreateController(
                NewConfig("hold"),
                ["raw text"],
                postProcessorFactory: _ => processor,
                historyFactory: _ => history);
            setup.Controller.Start();
            await setup.Controller.HandleDictationKeyAsync(true);
            Task stop = setup.Controller.HandleDictationKeyAsync(false);
            await postProcessingStarted.Task;

            Task shutdown = setup.Controller.ShutdownAsync();
            Task concurrentShutdown = setup.Controller.ShutdownAsync();

            Assert.Same(shutdown, concurrentShutdown);
            await Task.WhenAll(stop, shutdown).WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Empty(setup.Sink.Requests);
            Assert.Empty(history.Snapshot());
        }
        finally
        {
            release.TrySetResult();
            Directory.Delete(home, recursive: true);
        }
    }

    [Fact]
    public async Task ShutdownDrainsDeliveryAcceptedBeforeItStarted()
    {
        var deliveryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDelivery = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sink = new RecordingTextSink(async (_, _, _) =>
        {
            deliveryStarted.TrySetResult();
            await releaseDelivery.Task;
            return TextDeliveryResult.Delivered;
        });
        var setup = CreateController(NewConfig("hold"), ["accepted"], sink);
        setup.Controller.Start();
        await setup.Controller.HandleDictationKeyAsync(true);
        Task stop = setup.Controller.HandleDictationKeyAsync(false);
        await deliveryStarted.Task;

        Task shutdown = setup.Controller.ShutdownAsync();

        Assert.False(shutdown.IsCompleted);
        releaseDelivery.TrySetResult();
        await Task.WhenAll(stop, shutdown).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("accepted", Assert.Single(sink.Requests).Text);
    }

    [Fact]
    public async Task SleepCancelsRecordingAndWakeUsesLatestInputDevice()
    {
        Config initial = NewConfig("hold", inputDevice: "old mic");
        Config updated = NewConfig("hold", inputDevice: "new mic");
        var setup = CreateController(initial, ["after wake"]);
        setup.Controller.Start();
        setup.Keyboard.RaiseDictation(true);
        await setup.Controller.DrainAsync();

        Task suspend = setup.Controller.SuspendAsync();
        Assert.True(setup.Controller.IsSuspended);
        Assert.True(setup.Keyboard.Suspended);
        setup.Keyboard.RaiseDictation(false);
        setup.Keyboard.RaiseDictation(true);
        await suspend;

        Assert.False(setup.Controller.IsSessionActive);
        Assert.Equal(1, setup.Audio.StopCount);
        Assert.Empty(setup.Sink.Requests);

        await setup.Controller.ApplyConfigAsync(updated);
        await setup.Controller.ResumeAsync();
        Assert.False(setup.Controller.IsSuspended);
        Assert.False(setup.Keyboard.Suspended);

        setup.Keyboard.RaiseDictation(true);
        await setup.Controller.DrainAsync();
        Assert.True(setup.Controller.IsSessionActive);
        Assert.Equal(["old mic", "new mic"], setup.Audio.StartedDevices);

        setup.Keyboard.RaiseDictation(false);
        await setup.Controller.DrainAsync();
        Assert.Equal("after wake", Assert.Single(setup.Sink.Requests).Text);
        await setup.Controller.ShutdownAsync();
    }

    [Fact]
    public async Task SleepCancelsActiveTranscriptionWithoutDelivery()
    {
        var transcriptionStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var setup = CreateController(NewConfig("hold"), [], modelFactory: (_, _) =>
            Task.FromResult(new TranscriptionModel(async (_, cancellationToken) =>
            {
                transcriptionStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return "never";
            })));
        setup.Controller.Start();
        await setup.Controller.HandleDictationKeyAsync(true);
        Task stop = setup.Controller.HandleDictationKeyAsync(false);
        await transcriptionStarted.Task;

        Task suspend = setup.Controller.SuspendAsync();

        await Task.WhenAll(stop, suspend).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(setup.Controller.IsSuspended);
        Assert.False(setup.Controller.IsSessionActive);
        Assert.Empty(setup.Sink.Requests);
        await setup.Controller.ShutdownAsync();
    }

    [Fact]
    public async Task SleepCancelsActivePostProcessingWithoutDelivery()
    {
        var postProcessingStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var processor = new TextPostProcessor(
            async (_, cancellationToken) =>
            {
                postProcessingStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return "never";
            },
            10000);
        var setup = CreateController(
            NewConfig("hold"),
            ["raw"],
            postProcessorFactory: _ => processor);
        setup.Controller.Start();
        await setup.Controller.HandleDictationKeyAsync(true);
        Task stop = setup.Controller.HandleDictationKeyAsync(false);
        await postProcessingStarted.Task;

        Task suspend = setup.Controller.SuspendAsync();

        await Task.WhenAll(stop, suspend).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Empty(setup.Sink.Requests);
        Assert.False(setup.Controller.IsSessionActive);
        await setup.Controller.ShutdownAsync();
    }

    [Fact]
    public async Task DeliveryAcceptedBeforeSleepIsNotCancelled()
    {
        var deliveryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDelivery = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken deliveryCancellation = default;
        var sink = new RecordingTextSink(async (_, _, cancellationToken) =>
        {
            deliveryCancellation = cancellationToken;
            deliveryStarted.TrySetResult();
            await releaseDelivery.Task;
            return TextDeliveryResult.Delivered;
        });
        var setup = CreateController(NewConfig("hold"), ["accepted"], sink);
        setup.Controller.Start();
        await setup.Controller.HandleDictationKeyAsync(true);
        Task stop = setup.Controller.HandleDictationKeyAsync(false);
        await deliveryStarted.Task;

        Task suspend = setup.Controller.SuspendAsync();
        Assert.True(setup.Controller.IsSuspended);
        Assert.False(deliveryCancellation.IsCancellationRequested);
        releaseDelivery.TrySetResult();

        await Task.WhenAll(stop, suspend).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(deliveryCancellation.IsCancellationRequested);
        Assert.Equal("accepted", Assert.Single(sink.Requests).Text);
        await setup.Controller.ShutdownAsync();
    }

    [Fact]
    public async Task RepeatedAndOutOfOrderPowerTransitionsAreIdempotent()
    {
        var setup = CreateController(NewConfig("hold"), ["awake"]);
        setup.Controller.Start();

        await setup.Controller.ResumeAsync();
        Assert.False(setup.Controller.IsSuspended);

        Task firstSuspend = setup.Controller.SuspendAsync();
        Task secondSuspend = setup.Controller.SuspendAsync();
        await Task.WhenAll(firstSuspend, secondSuspend);
        Assert.True(setup.Controller.IsSuspended);
        Assert.True(setup.Keyboard.Suspended);

        Task firstResume = setup.Controller.ResumeAsync();
        Task secondResume = setup.Controller.ResumeAsync();
        await Task.WhenAll(firstResume, secondResume);
        Assert.False(setup.Controller.IsSuspended);
        Assert.False(setup.Keyboard.Suspended);

        setup.Keyboard.RaiseDictation(true);
        await setup.Controller.DrainAsync();
        Assert.True(setup.Controller.IsSessionActive);
        setup.Keyboard.RaiseDictation(false);
        await setup.Controller.DrainAsync();
        Assert.Equal("awake", Assert.Single(setup.Sink.Requests).Text);
        await setup.Controller.ShutdownAsync();
    }

    [Fact]
    public async Task SleepCompletesKeyboardReloadDeferredByActiveSession()
    {
        var setup = CreateController(NewConfig("hold"), []);
        var replacement = new FakeKeyboardHook();
        setup.Controller.Start();
        setup.Keyboard.RaiseDictation(true);
        await setup.Controller.DrainAsync();

        Task apply = setup.Controller.ApplyConfigAsync(NewConfig("toggle"), replacement);
        await setup.Controller.DrainAsync();
        Assert.False(apply.IsCompleted);

        await setup.Controller.SuspendAsync().WaitAsync(TimeSpan.FromSeconds(2));
        await apply.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(setup.Keyboard.Disposed);
        Assert.True(replacement.Started);
        Assert.True(replacement.Suspended);
        Assert.Equal("toggle", setup.Controller.CurrentConfig.Mode);

        await setup.Controller.ResumeAsync();
        Assert.False(replacement.Suspended);
        await setup.Controller.ShutdownAsync();
    }

    [Fact]
    public async Task ImmediateWakeQueuesAfterSleepCleanup()
    {
        var setup = CreateController(NewConfig("hold"), ["awake"]);
        setup.Controller.Start();
        setup.Keyboard.RaiseDictation(true);
        await setup.Controller.DrainAsync();

        Task suspend = setup.Controller.SuspendAsync();
        Task resume = setup.Controller.ResumeAsync();
        await Task.WhenAll(suspend, resume).WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(setup.Controller.IsSuspended);
        Assert.False(setup.Controller.IsSessionActive);
        Assert.Equal(1, setup.Audio.StopCount);
        setup.Keyboard.RaiseDictation(true);
        await setup.Controller.DrainAsync();
        Assert.True(setup.Controller.IsSessionActive);
        setup.Keyboard.RaiseDictation(false);
        await setup.Controller.DrainAsync();
        Assert.Equal("awake", Assert.Single(setup.Sink.Requests).Text);
        await setup.Controller.ShutdownAsync();
    }

    [Fact]
    public async Task ImmediateWakeActivatesKeyboardReloadDeferredBySleep()
    {
        var setup = CreateController(NewConfig("hold"), []);
        var replacement = new FakeKeyboardHook();
        setup.Controller.Start();
        setup.Keyboard.RaiseDictation(true);
        await setup.Controller.DrainAsync();
        Task apply = setup.Controller.ApplyConfigAsync(NewConfig("toggle"), replacement);
        await setup.Controller.DrainAsync();

        Task suspend = setup.Controller.SuspendAsync();
        Task resume = setup.Controller.ResumeAsync();
        await Task.WhenAll(suspend, resume, apply).WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(replacement.Started);
        Assert.False(replacement.Suspended);
        Assert.False(setup.Controller.IsSuspended);
        await setup.Controller.ShutdownAsync();
    }

    [Fact]
    public async Task SleepDuringLiveSessionDoesNotAddFinalEnter()
    {
        var deliveryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sink = new RecordingTextSink((_, _, _) =>
        {
            deliveryStarted.TrySetResult();
            return Task.FromResult(TextDeliveryResult.Delivered);
        });
        var setup = CreateController(NewConfig("push", autoEnter: true), ["spoken"], sink);
        setup.Controller.Start();
        await setup.Controller.HandleDictationKeyAsync(true);
        EmitPhrase(setup.Audio);
        await deliveryStarted.Task;

        await setup.Controller.SuspendAsync();

        var request = Assert.Single(sink.Requests);
        Assert.Equal("spoken ", request.Text);
        Assert.False(request.PressEnter);
        await setup.Controller.ShutdownAsync();
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
        string modelPath = "fake-model",
        string pinDelivery = "focus",
        string pasteMethod = "unicode",
        bool focusBorder = true,
        string focusColor = "#E81123",
        string busyColor = "#FFB900",
        int thickness = 4,
        float opacity = 0.9f,
        int historyMaxItems = 100,
        bool history = true,
        string postProcessProvider = "anthropic",
        string postProcessEndpoint = "",
        string uiLanguage = "pt-BR") => new()
    {
        ModelPath = modelPath,
        Mode = mode,
        AutoEnter = autoEnter,
        InputDevice = inputDevice,
        Beep = false,
        IdleUnloadMinutes = 0,
        PinDelivery = pinDelivery,
        PasteMethod = pasteMethod,
        FocusBorder = focusBorder,
        FocusBorderColor = focusColor,
        FocusBorderColorBusy = busyColor,
        FocusBorderThickness = thickness,
        FocusBorderOpacity = opacity,
        History = history,
        HistoryMaxItems = historyMaxItems,
        PostProcessProvider = postProcessProvider,
        PostProcessEndpoint = postProcessEndpoint,
        UiLanguage = uiLanguage,
        EffectiveUiLanguage = uiLanguage,
    };

    private static string NewTemporaryDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "matraca-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
