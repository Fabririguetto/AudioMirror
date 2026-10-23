using NAudio.CoreAudioApi;

namespace AudioMirror.Core;

/// <summary>
/// Sincroniza y nivela en tiempo real el volumen y estado de silencio (Mute)
/// entre el dispositivo de audio de origen y el de destino.
/// </summary>
public sealed class VolumeSyncService : IDisposable
{
    private readonly MMDevice _sourceDevice;
    private readonly MMDevice _targetDevice;
    private readonly AudioEndpointVolumeNotificationDelegate _notificationDelegate;
    private bool _isSyncing;
    private bool _disposed;

    public bool IsEnabled { get; set; } = true;

    public VolumeSyncService(MMDevice sourceDevice, MMDevice targetDevice)
    {
        _sourceDevice = sourceDevice ?? throw new ArgumentNullException(nameof(sourceDevice));
        _targetDevice = targetDevice ?? throw new ArgumentNullException(nameof(targetDevice));

        _notificationDelegate = new AudioEndpointVolumeNotificationDelegate(OnSourceVolumeNotification);

        // 1. Nivelación inmediata al instanciar (evita saltos bruscos que aturdan al cambiar de fuente)
        EqualizeTargetToSource();

        // 2. Suscribirse a eventos de cambio de volumen del dispositivo de origen
        try
        {
            _sourceDevice.AudioEndpointVolume.OnVolumeNotification += _notificationDelegate;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[VolumeSyncService] No se pudo suscribir a eventos de volumen: {ex.Message}");
        }
    }

    /// <summary>
    /// Iguala inmediatamente el volumen y mute del destino al nivel actual del origen.
    /// </summary>
    public void EqualizeTargetToSource()
    {
        if (_disposed || !IsEnabled) return;

        try
        {
            _isSyncing = true;
            float sourceVol = _sourceDevice.AudioEndpointVolume.MasterVolumeLevelScalar;
            bool sourceMute = _sourceDevice.AudioEndpointVolume.Mute;

            _targetDevice.AudioEndpointVolume.MasterVolumeLevelScalar = sourceVol;
            _targetDevice.AudioEndpointVolume.Mute = sourceMute;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[VolumeSyncService] Error al igualar volumen: {ex.Message}");
        }
        finally
        {
            _isSyncing = false;
        }
    }

    private void OnSourceVolumeNotification(AudioVolumeNotificationData data)
    {
        if (_disposed || !IsEnabled || _isSyncing) return;

        try
        {
            _isSyncing = true;

            // Ajustar volumen del destino si difiere significativamente (> 0.5%)
            if (Math.Abs(_targetDevice.AudioEndpointVolume.MasterVolumeLevelScalar - data.MasterVolume) > 0.005f)
            {
                _targetDevice.AudioEndpointVolume.MasterVolumeLevelScalar = data.MasterVolume;
            }

            // Sincronizar estado de silencio
            if (_targetDevice.AudioEndpointVolume.Mute != data.Muted)
            {
                _targetDevice.AudioEndpointVolume.Mute = data.Muted;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[VolumeSyncService] Error sincronizando volumen: {ex.Message}");
        }
        finally
        {
            _isSyncing = false;
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            try
            {
                _sourceDevice.AudioEndpointVolume.OnVolumeNotification -= _notificationDelegate;
            }
            catch
            {
            }
            _disposed = true;
        }
    }
}
