using NAudio.Wave;

namespace Matraca;

/// <summary>
/// Escuta o microfone so para medir o nivel, sem gravar nada. O Timer da UI consulta os
/// campos em vez de receber um evento por frame, evitando uma fila entre threads.
/// </summary>
internal sealed class MicMonitor : IDisposable
{
    private const int SampleRate = 16000;
    private const int FrameMs = 30;
    private const float PeakDecayPerSecond = 0.8f;

    private WaveInEvent? _waveIn;
    private float _level;
    private float _peak;
    private long _lastPeakTick;

    public bool IsRunning { get; private set; }
    public float Level => Volatile.Read(ref _level);
    public float Peak => Volatile.Read(ref _peak);

    public bool Start(int deviceNumber)
    {
        Stop();
        try
        {
            _waveIn = new WaveInEvent
            {
                DeviceNumber = deviceNumber,
                WaveFormat = new WaveFormat(SampleRate, 16, 1),
                BufferMilliseconds = FrameMs,
            };
            _waveIn.DataAvailable += OnData;
            _waveIn.StartRecording();
            _lastPeakTick = Environment.TickCount64;
            IsRunning = true;
            return true;
        }
        catch (Exception ex)
        {
            Logger.Warn($"Medidor de microfone nao subiu (dispositivo {deviceNumber}): {ex.Message}");
            Stop();
            return false;
        }
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        int count = e.BytesRecorded / 2;
        if (count == 0) return;

        var frame = new float[count];
        for (int i = 0; i < count; i++)
            frame[i] = BitConverter.ToInt16(e.Buffer, i * 2) / 32768f;

        float rms = AudioLevelAnalyzer.CalculateRms(frame);
        Volatile.Write(ref _level, rms);

        long now = Environment.TickCount64;
        float decayed = Volatile.Read(ref _peak)
            - PeakDecayPerSecond * (now - _lastPeakTick) / 1000f;
        _lastPeakTick = now;
        Volatile.Write(ref _peak, Math.Max(rms, Math.Max(0f, decayed)));
    }

    public void Stop()
    {
        IsRunning = false;
        if (_waveIn == null) return;
        _waveIn.DataAvailable -= OnData;
        try { _waveIn.StopRecording(); } catch { }
        try { _waveIn.Dispose(); } catch { }
        _waveIn = null;
        Volatile.Write(ref _level, 0f);
        Volatile.Write(ref _peak, 0f);
    }

    public void Dispose() => Stop();
}
