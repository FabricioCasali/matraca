using System.Diagnostics;
using Matraca.Core;

namespace Matraca.Mac.Platform.Audio;

internal sealed class MacMicrophoneMonitor : IDisposable
{
    private static readonly long MinimumFrameTicks = Stopwatch.Frequency / 30;
    private readonly MacAudioCapture _capture = new(bufferSamples: false);
    private readonly SpectrumAnalyzer _spectrum = new();
    private long _lastFrameAt;
    private int _disposed;

    public MacMicrophoneMonitor()
        => _capture.FrameCaptured += OnFrameCaptured;

    public event Action<float, float, float[]>? Frame;
    public bool IsRunning => _capture.IsCapturing;
    public IReadOnlyList<string> ListDevices() => _capture.ListDevices();

    public Task StartAsync(string? device, CancellationToken cancellationToken = default)
        => _capture.StartAsync(device, TimeSpan.Zero, cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken = default)
        => Task.Run(
            async () => await _capture.StopAsync(cancellationToken).ConfigureAwait(false),
            cancellationToken);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _capture.FrameCaptured -= OnFrameCaptured;
        _capture.Dispose();
        Frame = null;
    }

    private void OnFrameCaptured(ReadOnlyMemory<float> frame)
    {
        long now = Stopwatch.GetTimestamp();
        long previous = Volatile.Read(ref _lastFrameAt);
        if (now - previous < MinimumFrameTicks
            || Interlocked.CompareExchange(ref _lastFrameAt, now, previous) != previous)
            return;

        ReadOnlySpan<float> samples = frame.Span;
        float rms = AudioLevelAnalyzer.CalculateRms(samples);
        float peak = 0;
        foreach (float sample in samples) peak = Math.Max(peak, Math.Abs(sample));
        Frame?.Invoke(rms, peak, _spectrum.Analyze(samples));
    }
}
