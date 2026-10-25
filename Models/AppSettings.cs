using System.Text.Json.Serialization;
using System.Windows.Input;
using AudioMirror.Core;

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
    /// Nombre amigable de la fuente principal como respaldo si el ID cambia entre reinicios.
    /// </summary>
    public string? PrimaryDeviceName { get; set; }

    /// <summary>
    /// ID del MMDevice seleccionado como destino duplicado.
    /// </summary>
    public string? SecondaryDeviceId { get; set; }

    /// <summary>
    /// Nombre amigable del destino como respaldo si el ID cambia entre reinicios.
    /// </summary>
    public string? SecondaryDeviceName { get; set; }

    /// <summary>
    /// Latencia configurada del buffer WASAPI en milisegundos.
    /// </summary>
    public int TargetLatencyMs { get; set; } = 25;

    /// <summary>
    /// Modificadores de tecla configurados para el atajo global (ej. Ctrl + Alt).
    /// </summary>
    [JsonConverter(typeof(ModifierKeysJsonConverter))]
    public ModifierKeys HotKeyModifiers { get; set; } = ModifierKeys.Control | ModifierKeys.Alt;

    /// <summary>
    /// Tecla configurada para el atajo global (ej. A).
    /// </summary>
    [JsonConverter(typeof(KeyJsonConverter))]
    public Key HotKeyKey { get; set; } = Key.A;

    /// <summary>
    /// Indica si se debe mostrar una notificación flotante (OSD) en la pantalla al alternar salidas.
    /// </summary>
    public bool ShowOsdNotification { get; set; } = true;

    /// <summary>
    /// Lista de IDs de los dispositivos marcados para rotación mediante el atajo global.
    /// </summary>
    public List<string> CycleDeviceIds { get; set; } = new();

    /// <summary>
    /// Lista de nombres amigables de los dispositivos marcados para rotación como respaldo.
    /// </summary>
    public List<string> CycleDeviceNames { get; set; } = new();

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
