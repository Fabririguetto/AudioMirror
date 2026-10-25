using Application = System.Windows.Application;

namespace AudioMirror.Core;

/// <summary>
/// Gestiona la visualización de la ventana OSD (On-Screen Display) en la pantalla sin robar el foco.
/// </summary>
public static class OsdNotificationManager
{
    private static OsdNotificationWindow? _window;
    private static readonly object _lock = new();

    public static void Initialize()
    {
        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            lock (_lock)
            {
                if (_window == null)
                {
                    _window = new OsdNotificationWindow();
                }
            }
        });
    }

    public static void Show(string deviceName, float volumeScalar, string? secondaryDeviceName = null, bool isMirroring = false)
    {
        var app = Application.Current;
        if (app == null) return;

        app.Dispatcher.InvokeAsync(() =>
        {
            lock (_lock)
            {
                _window ??= new OsdNotificationWindow();
                _window.ShowNotification(deviceName, volumeScalar, secondaryDeviceName, isMirroring);
            }
        });
    }

    public static void Close()
    {
        var app = Application.Current;
        if (app == null) return;

        app.Dispatcher.InvokeAsync(() =>
        {
            lock (_lock)
            {
                if (_window != null)
                {
                    _window.Close();
                    _window = null;
                }
            }
        });
    }
}