using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace AudioMirror.Core;

public enum AudioBridgeState
{
    Stopped,
    Starting,
    Running,
    Stopping,
    Faulted
}

/// <summary>
/// Motor robusto de puente y clonación de audio en tiempo real de baja latencia con WASAPI Loopback y Render.
/// </summary>
public sealed class AudioBridgeEngine : IDisposable
{
    private readonly object _lock = new();

    private WasapiLoopbackCapture? _capture;
    private WasapiOut? _renderOut;
    private BufferedWaveProvider? _bufferedWaveProvider;
    private IWaveProvider? _finalWaveProvider;
    private VolumeSyncService? _volumeSync;

    private AudioBridgeState _state = AudioBridgeState.Stopped;
    private bool _syncVolume = true;
    private bool _disposed;

    /// <summary>
    /// Sincroniza y nivela en tiempo real el volumen del destino con el del origen para evitar saturaciones.
    /// </summary>
    public bool SyncVolume
    {
        get => _syncVolume;
        set
        {
            _syncVolume = value;
            if (_volumeSync != null)
            {
                _volumeSync.IsEnabled = value;
                if (value)
                {
                    _volumeSync.EqualizeTargetToSource();
                }
            }
        }
    }

    public AudioBridgeState State
    {
        get => _state;
        private set
        {
            if (_state != value)
            {
                _state = value;
                StateChanged?.Invoke(this, _state);
            }
        }
    }

    public WaveFormat? SourceFormat { get; private set; }
    public WaveFormat? TargetFormat { get; private set; }
    public bool IsResamplingActive { get; private set; }

    public event EventHandler<AudioBridgeState>? StateChanged;
    public event EventHandler<string>? ErrorOccurred;
    public event Action<float, float>? PeakLevelUpdated;

    /// <summary>
    /// Inicia la clonación de audio en tiempo real desde el dispositivo origen hacia el destino.
    /// </summary>
    public void Start(MMDevice sourceDevice, MMDevice targetDevice, int latencyMilliseconds = 25)
    {
        ArgumentNullException.ThrowIfNull(sourceDevice);
        ArgumentNullException.ThrowIfNull(targetDevice);

        if (sourceDevice.ID.Equals(targetDevice.ID, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("El dispositivo de origen y destino no pueden ser el mismo.");
        }

        lock (_lock)
        {
            ThrowIfDisposed();

            if (State == AudioBridgeState.Running || State == AudioBridgeState.Starting)
            {
                Stop();
            }

            State = AudioBridgeState.Starting;

            try
            {
                // 1. Iniciar captura WASAPI Loopback en el dispositivo origen
                _capture = new WasapiLoopbackCapture(sourceDevice);
                SourceFormat = _capture.WaveFormat;

                // 2. Formato nativo del destino
                try
                {
                    TargetFormat = targetDevice.AudioClient.MixFormat;
                }
                catch
                {
                    TargetFormat = SourceFormat;
                }

                // 3. Crear buffer circular thread-safe
                // ReadFully = true garantiza que si el buffer se vacía momentáneamente,
                // entregue silencio (ceros) en lugar de retornar 0 bytes (lo que detendría WasapiOut).
                _bufferedWaveProvider = new BufferedWaveProvider(SourceFormat)
                {
                    BufferDuration = TimeSpan.FromMilliseconds(300),
                    DiscardOnBufferOverflow = true, // Elimina el retardo acumulativo por desincronización de relojes
                    ReadFully = true
                };

                // 4. Negociación de formato:
                // Verificamos si WASAPI Shared Mode en el dispositivo destino acepta directamente el formato de captura
                bool directSupported = targetDevice.AudioClient.IsFormatSupported(AudioClientShareMode.Shared, SourceFormat);

                if (directSupported)
                {
                    IsResamplingActive = false;
                    _finalWaveProvider = _bufferedWaveProvider;
                }
                else
                {
                    // Si el hardware requiere una tasa de muestreo distinta (ej. 44100 vs 48000),
                    // usamos WdlResamplingSampleProvider que es continuo y no sufre de caídas por fin de flujo (EOF).
                    IsResamplingActive = true;
                    var sampleProvider = _bufferedWaveProvider.ToSampleProvider();
                    var resampler = new WdlResamplingSampleProvider(sampleProvider, TargetFormat.SampleRate);
                    _finalWaveProvider = resampler.ToWaveProvider();
                }

                // 5. Configurar el cliente de reproducción WASAPI en el destino
                // useEventSync: false usa un hilo temporizador de precisión alta que evita bloqueos de hardware en HDMI/USB
                _renderOut = new WasapiOut(
                    targetDevice,
                    AudioClientShareMode.Shared,
                    useEventSync: false,
                    latency: Math.Clamp(latencyMilliseconds, 20, 100)
                );

                _renderOut.Init(_finalWaveProvider);

                // 6. Enlazar eventos
                _capture.DataAvailable += OnDataAvailable;
                _capture.RecordingStopped += OnRecordingStopped;
                _renderOut.PlaybackStopped += OnPlaybackStopped;

                // 7. Nivelar y sincronizar volúmenes de Windows en tiempo real (evita saturación y desbalance)
                _volumeSync = new VolumeSyncService(sourceDevice, targetDevice)
                {
                    IsEnabled = _syncVolume
                };

                // 8. Arrancar captura primero para que comiencen a entrar paquetes, luego el render
                _capture.StartRecording();
                _renderOut.Play();

                State = AudioBridgeState.Running;
            }
            catch (Exception ex)
            {
                CleanupInternal();
                State = AudioBridgeState.Faulted;
                ErrorOccurred?.Invoke(this, $"Error al iniciar puente de audio: {ex.Message}");
                throw;
            }
        }
    }

    /// <summary>
    /// Detiene la clonación y libera los clientes de captura y render.
    /// </summary>
    public void Stop()
    {
        lock (_lock)
        {
            if (State == AudioBridgeState.Stopped || State == AudioBridgeState.Stopping)
                return;

            State = AudioBridgeState.Stopping;
            CleanupInternal();
            State = AudioBridgeState.Stopped;
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded == 0 || _bufferedWaveProvider == null)
            return;

        _bufferedWaveProvider.AddSamples(e.Buffer, 0, e.BytesRecorded);
        ComputePeakLevels(e.Buffer, e.BytesRecorded, SourceFormat);
    }

    private void ComputePeakLevels(byte[] buffer, int count, WaveFormat? format)
    {
        if (format == null || PeakLevelUpdated == null)
            return;

        float maxLeft = 0.0f;
        float maxRight = 0.0f;

        if (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
        {
            ReadOnlySpan<float> samples = MemoryMarshal.Cast<byte, float>(buffer.AsSpan(0, count));
            int channels = format.Channels;
            for (int i = 0; i < samples.Length; i += channels)
            {
                float left = Math.Abs(samples[i]);
                if (left > maxLeft) maxLeft = left;

                if (channels > 1 && i + 1 < samples.Length)
                {
                    float right = Math.Abs(samples[i + 1]);
                    if (right > maxRight) maxRight = right;
                }
                else
                {
                    maxRight = maxLeft;
                }
            }
        }
        else if (format.BitsPerSample == 16)
        {
            ReadOnlySpan<short> samples = MemoryMarshal.Cast<byte, short>(buffer.AsSpan(0, count));
            int channels = format.Channels;
            for (int i = 0; i < samples.Length; i += channels)
            {
                float left = Math.Abs(samples[i]) / 32768.0f;
                if (left > maxLeft) maxLeft = left;

                if (channels > 1 && i + 1 < samples.Length)
                {
                    float right = Math.Abs(samples[i + 1]) / 32768.0f;
                    if (right > maxRight) maxRight = right;
                }
                else
                {
                    maxRight = maxLeft;
                }
            }
        }

        PeakLevelUpdated?.Invoke(Math.Clamp(maxLeft, 0f, 1f), Math.Clamp(maxRight, 0f, 1f));
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception != null && State == AudioBridgeState.Running)
        {
            State = AudioBridgeState.Faulted;
            ErrorOccurred?.Invoke(this, $"Captura detenida: {e.Exception.Message}");
        }
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception != null && State == AudioBridgeState.Running)
        {
            State = AudioBridgeState.Faulted;
            ErrorOccurred?.Invoke(this, $"Reproducción detenida: {e.Exception.Message}");
        }
    }

    private void CleanupInternal()
    {
        try
        {
            if (_capture != null)
            {
                _capture.DataAvailable -= OnDataAvailable;
                _capture.RecordingStopped -= OnRecordingStopped;
                _capture.StopRecording();
                _capture.Dispose();
                _capture = null;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error liberando captura: {ex.Message}");
        }

        try
        {
            if (_renderOut != null)
            {
                _renderOut.PlaybackStopped -= OnPlaybackStopped;
                _renderOut.Stop();
                _renderOut.Dispose();
                _renderOut = null;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error liberando salida: {ex.Message}");
        }

        try
        {
            if (_volumeSync != null)
            {
                _volumeSync.Dispose();
                _volumeSync = null;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error liberando sincronización de volumen: {ex.Message}");
        }

        _bufferedWaveProvider = null;
        _finalWaveProvider = null;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(AudioBridgeEngine));
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (!_disposed)
            {
                Stop();
                _disposed = true;
            }
        }
    }
}
