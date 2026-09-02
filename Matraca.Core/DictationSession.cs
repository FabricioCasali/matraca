using System.Collections.Concurrent;

namespace Matraca.Core;

internal sealed class DictationSession
{
    public required Config Config { get; init; }
    public required Task<TranscriptionModel?> Model { get; init; }
    public required TextPostProcessor? PostProcessor { get; init; }
    public required DictationHistory? History { get; init; }
    public bool Streaming { get; init; }
    public VoiceActivityDetector? Detector { get; set; }
    public BlockingCollection<float[]>? Segments { get; set; }
    public Task? Consumer { get; set; }
    public bool DeliveredSpeech { get; set; }
    public bool DeliveryFailed { get; set; }
}
