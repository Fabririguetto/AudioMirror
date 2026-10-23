namespace AudioMirror.Models;

/// <summary>
/// Modelo fuertemente tipado para la persistencia de configuración del usuario.
/// </summary>
public sealed class AppSettings
{
    /// <summary>
    /// ID del MMDevice seleccionado como fuente principal.
    /// </summary>
    public string? PrimaryDeviceId { get; set; }

    /// <summary>
    /// ID del MMDevice seleccionado como destino duplicado.
    /// </summary>
    public string? SecondaryDeviceId { get; set; }

    /// <summary>
    /// Latencia configurada del buffer WASAPI en milisegundos.
    /// </summary>
    public int TargetLatencyMs { get; set; } = 25;

    /// <summary>
    /// Lista de IDs de los dispositivos marcados para rotación mediante el atajo global (Ctrl + Alt + A).
    /// </summary>
    public List<string> CycleDeviceIds { get; set; } = new();

    /// <summary>
    /// Indica si la duplicación de audio debe arrancar de forma desatendida al iniciar la aplicación.
    /// </summary>
    public bool AutoStartMirroring { get; set; } = false;

    /// <summary>
    /// Indica si la aplicación debe registrarse para arrancar automáticamente con Windows.
    /// </summary>
    public bool StartWithWindows { get; set; } = false;

    /// <summary>
    /// Indica si la aplicación debe iniciarse directamente oculta en la bandeja del sistema.
    /// </summary>
    public bool StartMinimized { get; set; } = false;

    /// <summary>
    /// Indica si se debe sincronizar el volumen entre ambos dispositivos.
    /// </summary>
    public bool SyncVolume { get; set; } = true;

    /// <summary>
    /// Indica si la ventana debe ocultarse en la bandeja del sistema al pulsar el botón de cerrar [X].
    /// </summary>
    public bool MinimizeToTrayOnClose { get; set; } = true;
}
