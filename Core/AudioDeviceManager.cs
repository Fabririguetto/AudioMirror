using NAudio.CoreAudioApi;

namespace AudioMirror.Core;

/// <summary>
/// Gestiona la enumeración e inspección de dispositivos de audio multimedia activos (MMDevices) en Windows.
/// </summary>
public sealed class AudioDeviceManager : IDisposable
{
    private readonly MMDeviceEnumerator _enumerator;
    private bool _disposed;

    public AudioDeviceManager()
    {
        _enumerator = new MMDeviceEnumerator();
    }

    /// <summary>
    /// Obtiene todos los endpoints de salida (Render) activos en el sistema.
    /// </summary>
    public IReadOnlyList<AudioDeviceInfo> GetActiveRenderDevices()
    {
        ThrowIfDisposed();

        var result = new List<AudioDeviceInfo>();
        string? defaultDeviceId = null;

        try
        {
            var defaultDevice = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            defaultDeviceId = defaultDevice?.ID;
        }
        catch
        {
            // Puede fallar si no hay ningún dispositivo predeterminado conectado
        }

        var collection = _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
        foreach (var device in collection)
        {
            bool isDefault = !string.IsNullOrEmpty(defaultDeviceId) && device.ID.Equals(defaultDeviceId, StringComparison.OrdinalIgnoreCase);
            result.Add(new AudioDeviceInfo(device, isDefault));
        }

        // Ordenar: primero el predeterminado, luego alfabéticamente
        return result
            .OrderByDescending(d => d.IsDefault)
            .ThenBy(d => d.FriendlyName)
            .ToList();
    }

    /// <summary>
    /// Obtiene el dispositivo predeterminado actual de reproducción.
    /// </summary>
    public MMDevice? GetDefaultRenderDevice()
    {
        ThrowIfDisposed();
        try
        {
            return _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        }
        catch
        {
            return null;
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(AudioDeviceManager));
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _enumerator.Dispose();
            _disposed = true;
        }
    }
}
