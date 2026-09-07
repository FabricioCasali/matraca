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
    private Timer? _watchdog;
    private long _lastAudioAt;

    public MacMicrophoneMonitor()
        => _capture.FrameCaptured += OnFrameCaptured;

    public event Action<float, float, float[]>? Frame;
    public event Action<int, string>? Error;
    public string? CurrentDevice => _capture.CurrentDevice;
    public bool IsRunning => _capture.IsCapturing;
    public IReadOnlyList<string> ListDevices() => _capture.ListDevices();

    public async Task StartAsync(string? device, CancellationToken cancellationToken = default, int generation = 0)
    {
        await _capture.StartAsync(device, TimeSpan.Zero, cancellationToken).ConfigureAwait(false);
        Volatile.Write(ref _lastAudioAt, Environment.TickCount64);
        Timer? watchdog = null;
        watchdog = new Timer(_ =>
        {
            if (!_capture.IsCapturing || Environment.TickCount64 - Volatile.Read(ref _lastAudioAt) > 5000
                || !ListDevices().Contains(CurrentDevice, StringComparer.OrdinalIgnoreCase))
            {
                if (Interlocked.CompareExchange(ref _watchdog, null, watchdog) != watchdog) return;
                watchdog?.Dispose();
                Error?.Invoke(generation, "O microfone parou de fornecer audio. Confira a conexao e tente novamente.");
            }
        }, null, 1000, 1000);
        _watchdog = watchdog;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Exchange(ref _watchdog, null)?.Dispose();
        return Task.Run(
            async () => await _capture.StopAsync(cancellationToken).ConfigureAwait(false),
            cancellationToken);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Interlocked.Exchange(ref _watchdog, null)?.Dispose();
        _capture.FrameCaptured -= OnFrameCaptured;
        _capture.Dispose();
        Frame = null;
    }

    private void OnFrameCaptured(ReadOnlyMemory<float> frame)
    {
        Volatile.Write(ref _lastAudioAt, Environment.TickCount64);
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
