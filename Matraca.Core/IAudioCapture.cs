namespace Matraca.Core;

public interface IAudioCapture : IDisposable
{
    const int RequiredSampleRate = 16000;

    /// <summary>Frames are mono float samples at 16 kHz and remain valid after the callback returns.</summary>
    event Action<ReadOnlyMemory<float>>? FrameCaptured;

    bool IsCapturing { get; }
    IReadOnlyList<string> ListDevices();
    /// <summary>Completes after capture is active; platform startup must not block a vital app thread.</summary>
    Task StartAsync(string? deviceName, TimeSpan initialMute, CancellationToken cancellationToken = default);

    /// <summary>Completes after capture has stopped and returns every accepted sample in order.</summary>
    Task<float[]> StopAsync(CancellationToken cancellationToken = default);
}
