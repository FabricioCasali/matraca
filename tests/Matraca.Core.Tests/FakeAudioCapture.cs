using Matraca.Core;

namespace Matraca.Core.Tests;

internal sealed class FakeAudioCapture : IAudioCapture
{
    private readonly List<float> _samples = new();

    public event Action<ReadOnlyMemory<float>>? FrameCaptured;

    public bool IsCapturing { get; private set; }
    public string? CurrentDevice { get; set; }
    public float? StartupFrameValue { get; set; }
    public Action? DrainFramesOnStop { get; set; }
    public int StartCount { get; private set; }
    public int StopCount { get; private set; }
    public string? LastDevice { get; private set; }
    public List<string?> StartedDevices { get; } = new();
    public TimeSpan LastInitialMute { get; private set; }
    public bool Disposed { get; private set; }

    public IReadOnlyList<string> ListDevices() => ["fake microphone"];

    public Task StartAsync(
        string? deviceName,
        TimeSpan initialMute,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _samples.Clear();
        IsCapturing = true;
        StartCount++;
        LastDevice = deviceName;
        StartedDevices.Add(deviceName);
        LastInitialMute = initialMute;
        if (StartupFrameValue is float value) Emit(value, 10);
        return Task.CompletedTask;
    }

    public Task<float[]> StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DrainFramesOnStop?.Invoke();
        IsCapturing = false;
        StopCount++;
        return Task.FromResult(_samples.ToArray());
    }

    public void Emit(float value, int frameCount)
    {
        for (int index = 0; index < frameCount; index++)
        {
            var frame = Enumerable.Repeat(value, VoiceActivityDetector.FrameSampleCount).ToArray();
            _samples.AddRange(frame);
            FrameCaptured?.Invoke(frame);
        }
    }

    public void Dispose() => Disposed = true;
}
