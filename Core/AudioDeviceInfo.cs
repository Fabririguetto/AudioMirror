using System.ComponentModel;
using System.Runtime.CompilerServices;
using NAudio.CoreAudioApi;

namespace AudioMirror.Core;

/// <summary>
/// Representa la información descriptiva y de configuración de un endpoint de reproducción de audio.
/// </summary>
public sealed class AudioDeviceInfo : INotifyPropertyChanged
{
    private bool _isIncludedInCycle;
    private bool _isDefault;

    public string Id { get; }
    public string FriendlyName { get; }
    public MMDevice Device { get; }
    public string FormatDescription { get; }

    /// <summary>
    /// Indica si este es el dispositivo de reproducción predeterminado de Windows.
    /// </summary>
    public bool IsDefault
    {
        get => _isDefault;
        set
        {
            if (_isDefault != value)
            {
                _isDefault = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Indica si este dispositivo debe participar en el ciclo rotativo del atajo de teclado.
    /// </summary>
    public bool IsIncludedInCycle
    {
        get => _isIncludedInCycle;
        set
        {
            if (_isIncludedInCycle != value)
            {
                _isIncludedInCycle = value;
                OnPropertyChanged();
            }
        }
    }

    public AudioDeviceInfo(MMDevice device, bool isDefault)
    {
        Device = device ?? throw new ArgumentNullException(nameof(device));
        Id = device.ID;
        FriendlyName = device.FriendlyName;
        _isDefault = isDefault;

        // Por defecto, incluir salidas físicas (HDMI, altavoces, auriculares) y no puertos ópticos vacíos
        IsIncludedInCycle = !FriendlyName.Contains("Digital Output", StringComparison.OrdinalIgnoreCase);

        try
        {
            var mixFormat = device.AudioClient?.MixFormat;
            FormatDescription = mixFormat != null
                ? $"{mixFormat.SampleRate} Hz, {mixFormat.Channels} Ch, {mixFormat.BitsPerSample}-bit"
                : "Formato desconocido";
        }
        catch
        {
            FormatDescription = "No accesible";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public override string ToString()
    {
        return IsDefault ? $"{FriendlyName} [Predeterminado]" : FriendlyName;
    }
}
