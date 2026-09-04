using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using Matraca.Core;
using Matraca.Mac.Platform.Interop;

namespace Matraca.Mac.Platform.Audio;

internal sealed class MacAudioCapture : IAudioCapture
{
    private const int BufferCount = 3;
    private const int BufferMilliseconds = VoiceActivityDetector.FrameMilliseconds;
    private const int BytesPerSample = 2;
    private const int BufferBytes =
        IAudioCapture.RequiredSampleRate * BufferMilliseconds / 1000 * BytesPerSample;
    private const int FlowProbeSamples = IAudioCapture.RequiredSampleRate / 2;
    private static readonly TimeSpan FlowTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(2);

    private readonly object _nativeGate = new();
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly List<float> _captured = new();
    private readonly bool _bufferSamples;

    private Channel<short[]>? _frames;
    private Task? _frameProcessor;
    private TaskCompletionSource<bool>? _flowProbe;
    private GCHandle _callbackHandle;
    private IntPtr _queue;
    private long _muteSamplesRemaining;
    private int _probedSamples;
    private int _callbackStatus;
    private int _acceptingBuffers;
    private int _requeueBuffers;
    private int _capturing;
    private int _disposed;

    public event Action<ReadOnlyMemory<float>>? FrameCaptured;

    public MacAudioCapture(bool bufferSamples = true)
        => _bufferSamples = bufferSamples;

    public bool IsCapturing => Volatile.Read(ref _capturing) != 0;

    public IReadOnlyList<string> ListDevices() => MacAudioDevices.List()
        .Select(device => device.Name)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public async Task StartAsync(
        string? deviceName,
        TimeSpan initialMute,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (_queue != IntPtr.Zero || IsCapturing)
                throw new InvalidOperationException("A captura de audio ja esta ativa.");

            PrepareSession(initialMute);
            try
            {
                CancellationTokenRegistration registration =
                    cancellationToken.Register(CancelNativeStart);
                try
                {
                    await Task.Factory.StartNew(
                            () => StartNative(deviceName, cancellationToken),
                            CancellationToken.None,
                            TaskCreationOptions.DenyChildAttach | TaskCreationOptions.LongRunning,
                            TaskScheduler.Default)
                        .ConfigureAwait(false);

                    TaskCompletionSource<bool> probe = _flowProbe
                        ?? throw new InvalidOperationException("A sonda de audio nao foi criada.");
                    bool hasFlow;
                    try
                    {
                        hasFlow = await probe.Task
                            .WaitAsync(FlowTimeout, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (TimeoutException exception)
                    {
                        throw new InvalidOperationException(
                            "O AudioQueue iniciou, mas nao entregou frames do microfone.",
                            exception);
                    }

                    if (!hasFlow)
                        throw new UnauthorizedAccessException(
                            "O fluxo inicial do AudioQueue contem somente zeros; confira a permissao e o microfone.");
                }
                finally
                {
                    registration.Dispose();
                }

                cancellationToken.ThrowIfCancellationRequested();
            }
            catch
            {
                await CleanupSessionAsync(flushNativeBuffer: false).ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async Task<float[]> StopAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_queue == IntPtr.Zero) return Array.Empty<float>();

            await CleanupSessionAsync(flushNativeBuffer: true).ConfigureAwait(false);
            ThrowIfCallbackFailed();
            return _captured.ToArray();
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    private void PrepareSession(TimeSpan initialMute)
    {
        _captured.Clear();
        _probedSamples = 0;
        _callbackStatus = 0;
        _muteSamplesRemaining = Math.Max(
            0L,
            (long)(initialMute.TotalSeconds * IAudioCapture.RequiredSampleRate));
        _flowProbe = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _frames = Channel.CreateUnbounded<short[]>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
        _frameProcessor = Task.Run(() => ProcessFramesAsync(_frames.Reader));
        _callbackHandle = GCHandle.Alloc(this);
    }

    private unsafe void StartNative(string? deviceName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Frameworks.EnsureLoaded();

        string wantedDevice = (deviceName ?? string.Empty).Trim();
        MacAudioDevice? selectedDevice = ResolveDevice(wantedDevice);

        AudioStreamBasicDescription format =
            AudioStreamBasicDescription.Pcm16Mono(IAudioCapture.RequiredSampleRate);
        int status = AudioToolbox.AudioQueueNewInput(
            ref format,
            &OnBuffer,
            GCHandle.ToIntPtr(_callbackHandle),
            IntPtr.Zero,
            IntPtr.Zero,
            0,
            out IntPtr queue);
        ThrowIfNativeFailed("AudioQueueNewInput", status);

        lock (_nativeGate) _queue = queue;
        if (selectedDevice != null)
            SelectDevice(queue, selectedDevice);
        for (int index = 0; index < BufferCount; index++)
        {
            status = AudioToolbox.AudioQueueAllocateBuffer(queue, BufferBytes, out AudioQueueBuffer* buffer);
            ThrowIfNativeFailed("AudioQueueAllocateBuffer", status);
            status = AudioToolbox.AudioQueueEnqueueBuffer(queue, buffer, 0, IntPtr.Zero);
            ThrowIfNativeFailed("AudioQueueEnqueueBuffer", status);
        }

        cancellationToken.ThrowIfCancellationRequested();
        Volatile.Write(ref _acceptingBuffers, 1);
        Volatile.Write(ref _requeueBuffers, 1);
        status = AudioToolbox.AudioQueueStart(queue, IntPtr.Zero);
        if (status != 0)
        {
            Volatile.Write(ref _acceptingBuffers, 0);
            Volatile.Write(ref _requeueBuffers, 0);
            ThrowIfNativeFailed("AudioQueueStart", status);
        }

        Volatile.Write(ref _capturing, 1);
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static MacAudioDevice? ResolveDevice(string wantedDevice)
    {
        if (wantedDevice.Length == 0)
        {
            Logger.Info("Captura de audio usando o microfone padrao do macOS.");
            return null;
        }

        IReadOnlyList<MacAudioDevice> devices = MacAudioDevices.List();
        MacAudioDevice? selected = MacAudioDeviceSelector.Find(devices, wantedDevice);
        if (selected == null)
        {
            Logger.Warn(
                $"Microfone '{wantedDevice}' ausente ou desconectado; usando o padrao do macOS.");
            return null;
        }

        int matches = devices.Count(device =>
            string.Equals(device.Name, wantedDevice, StringComparison.OrdinalIgnoreCase));
        if (matches > 1)
            Logger.Warn(
                $"Ha {matches} microfones chamados '{selected.Name}'; usando o primeiro UID estavel.");
        return selected;
    }

    private static unsafe void SelectDevice(IntPtr queue, MacAudioDevice device)
    {
        IntPtr uid = IntPtr.Zero;
        try
        {
            uid = CoreFoundation.CreateString(device.Uid);

            IntPtr value = uid;
            int status = AudioToolbox.AudioQueueSetProperty(
                queue,
                AudioToolbox.PropertyCurrentDevice,
                (IntPtr)(&value),
                (uint)IntPtr.Size);
            if (status != 0)
            {
                Logger.Warn(
                    $"Microfone '{device.Name}' ficou indisponivel durante a selecao " +
                    $"({AudioToolbox.DescribeStatus(status)}); usando o padrao do macOS.");
                return;
            }

            Logger.Info($"Captura de audio usando o microfone configurado '{device.Name}'.");
        }
        catch (Exception exception)
        {
            Logger.Warn(
                $"Falha ao selecionar o microfone '{device.Name}': {exception.Message}; " +
                "usando o padrao do macOS.");
        }
        finally
        {
            CoreFoundation.Release(uid);
        }
    }

    private async Task ProcessFramesAsync(ChannelReader<short[]> reader)
    {
        try
        {
            await foreach (short[] pcm in reader.ReadAllAsync().ConfigureAwait(false))
            {
                ProbeFlow(pcm);

                int offset = 0;
                if (_muteSamplesRemaining > 0)
                {
                    int skipped = (int)Math.Min(_muteSamplesRemaining, pcm.Length);
                    _muteSamplesRemaining -= skipped;
                    offset = skipped;
                }

                if (offset == pcm.Length) continue;
                var samples = new float[pcm.Length - offset];
                for (int index = 0; index < samples.Length; index++)
                    samples[index] = pcm[offset + index] / 32768f;

                if (_bufferSamples) _captured.AddRange(samples);
                try { FrameCaptured?.Invoke(samples); }
                catch (Exception exception) { Logger.Error("Falha ao consumir frame de audio", exception); }
            }
        }
        catch (Exception exception)
        {
            _flowProbe?.TrySetException(exception);
            Logger.Error("Worker de audio do Mac falhou", exception);
        }
    }

    private void ProbeFlow(short[] pcm)
    {
        TaskCompletionSource<bool>? probe = _flowProbe;
        if (probe == null || probe.Task.IsCompleted) return;

        if (Array.Exists(pcm, sample => sample != 0))
        {
            probe.TrySetResult(true);
            return;
        }

        _probedSamples += pcm.Length;
        if (_probedSamples >= FlowProbeSamples) probe.TrySetResult(false);
    }

    private async Task CleanupSessionAsync(bool flushNativeBuffer)
    {
        Volatile.Write(ref _capturing, 0);

        int stopStatus = 0;
        int disposeStatus = 0;
        Exception? stopException = null;
        lock (_nativeGate)
        {
            if (_queue != IntPtr.Zero)
            {
                Volatile.Write(ref _requeueBuffers, 0);
                stopStatus = AudioToolbox.AudioQueueStop(
                    _queue,
                    immediate: !flushNativeBuffer);
                if (stopStatus == 0 && flushNativeBuffer)
                {
                    try { WaitUntilStopped(_queue); }
                    catch (Exception exception)
                    {
                        stopException = exception;
                        AudioToolbox.AudioQueueStop(_queue, immediate: true);
                    }
                }
                disposeStatus = AudioToolbox.AudioQueueDispose(_queue, immediate: true);
                if (disposeStatus == 0)
                {
                    _queue = IntPtr.Zero;
                    Volatile.Write(ref _acceptingBuffers, 0);
                }
            }
            else
            {
                Volatile.Write(ref _requeueBuffers, 0);
                Volatile.Write(ref _acceptingBuffers, 0);
            }
        }

        // The callback owns the GCHandle until native disposal proves it is quiescent.
        ThrowIfNativeFailed("AudioQueueDispose", disposeStatus);
        _frames?.Writer.TryComplete();
        if (_frameProcessor != null)
            await _frameProcessor.ConfigureAwait(false);

        if (_callbackHandle.IsAllocated) _callbackHandle.Free();
        _frames = null;
        _frameProcessor = null;
        _flowProbe = null;
        ThrowIfNativeFailed("AudioQueueStop", stopStatus);
        if (stopException != null) throw stopException;
    }

    private static void WaitUntilStopped(IntPtr queue)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < StopTimeout)
        {
            uint size = sizeof(uint);
            int status = AudioToolbox.AudioQueueGetProperty(
                queue,
                AudioToolbox.PropertyIsRunning,
                out uint isRunning,
                ref size);
            ThrowIfNativeFailed("AudioQueueGetProperty(IsRunning)", status);
            if (isRunning == 0) return;
            Thread.Sleep(10);
        }

        throw new TimeoutException("O AudioQueue nao concluiu o flush em 2 segundos.");
    }

    private void CancelNativeStart()
    {
        lock (_nativeGate)
        {
            if (_queue != IntPtr.Zero)
            {
                Volatile.Write(ref _requeueBuffers, 0);
                AudioToolbox.AudioQueueStop(_queue, immediate: true);
            }
        }
    }

    private unsafe void AcceptBuffer(IntPtr queue, AudioQueueBuffer* buffer)
    {
        int byteCount = checked((int)buffer->AudioDataByteSize);
        if (byteCount > 0 && Volatile.Read(ref _acceptingBuffers) != 0)
        {
            var copy = new short[byteCount / BytesPerSample];
            Marshal.Copy(buffer->AudioData, copy, 0, copy.Length);
            _frames?.Writer.TryWrite(copy);
        }

        if (Volatile.Read(ref _requeueBuffers) == 0) return;
        int status = AudioToolbox.AudioQueueEnqueueBuffer(queue, buffer, 0, IntPtr.Zero);
        if (status != 0)
        {
            Interlocked.CompareExchange(ref _callbackStatus, status, 0);
            _flowProbe?.TrySetException(new InvalidOperationException(
                $"AudioQueueEnqueueBuffer falhou: {AudioToolbox.DescribeStatus(status)}"));
        }
    }

    private void ThrowIfCallbackFailed()
    {
        int status = Volatile.Read(ref _callbackStatus);
        if (status != 0)
            throw new InvalidOperationException(
                $"AudioQueueEnqueueBuffer falhou: {AudioToolbox.DescribeStatus(status)}");
    }

    private static void ThrowIfNativeFailed(string operation, int status)
    {
        if (status != 0)
            throw new InvalidOperationException(
                $"{operation} falhou: {AudioToolbox.DescribeStatus(status)}");
    }

    [UnmanagedCallersOnly]
    private static unsafe void OnBuffer(
        IntPtr userData,
        IntPtr queue,
        AudioQueueBuffer* buffer,
        IntPtr startTime,
        uint packetDescriptionCount,
        IntPtr packetDescriptions)
    {
        try
        {
            if (GCHandle.FromIntPtr(userData).Target is MacAudioCapture capture)
                capture.AcceptBuffer(queue, buffer);
        }
        catch (Exception exception)
        {
            try { Logger.Error("Callback do AudioQueue falhou", exception); }
            catch { }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        _lifecycle.Wait();
        try { CleanupSessionAsync(flushNativeBuffer: false).GetAwaiter().GetResult(); }
        catch (Exception exception) { Logger.Error("Falha ao encerrar captura do Mac", exception); }
        finally
        {
            _lifecycle.Release();
            _lifecycle.Dispose();
        }
    }
}
