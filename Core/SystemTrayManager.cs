using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace AudioMirror.Core;

/// <summary>
/// Gestiona la presencia de la aplicación en la bandeja del sistema (System Tray)
/// para ejecución en segundo plano y notificaciones flotantes.
/// </summary>
public sealed class SystemTrayManager : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _statusMenuItem;
    private readonly ToolStripMenuItem _toggleBridgeMenuItem;
    private readonly ToolStripMenuItem _nextSourceMenuItem;
    private readonly Icon? _customIcon;
    private bool _disposed;

    public event Action? OpenRequested;
    public event Action? NextSourceRequested;
    public event Action? ToggleBridgeRequested;
    public event Action? ExitRequested;

    public SystemTrayManager()
    {
        var contextMenu = new ContextMenuStrip();

        var openItem = new ToolStripMenuItem("Abrir AudioMirror", null, (_, _) => OpenRequested?.Invoke());
        openItem.Font = new Font(openItem.Font, System.Drawing.FontStyle.Bold);
        contextMenu.Items.Add(openItem);

        contextMenu.Items.Add(new ToolStripSeparator());

        _nextSourceMenuItem = new ToolStripMenuItem("Siguiente fuente de audio (Ctrl+Alt+A)", null, (_, _) => NextSourceRequested?.Invoke());
        contextMenu.Items.Add(_nextSourceMenuItem);

        _toggleBridgeMenuItem = new ToolStripMenuItem("Iniciar Duplicación", null, (_, _) => ToggleBridgeRequested?.Invoke());
        contextMenu.Items.Add(_toggleBridgeMenuItem);

        contextMenu.Items.Add(new ToolStripSeparator());

        _statusMenuItem = new ToolStripMenuItem("Estado: Detenido") { Enabled = false };
        contextMenu.Items.Add(_statusMenuItem);

        contextMenu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("Salir", null, (_, _) => ExitRequested?.Invoke());
        contextMenu.Items.Add(exitItem);

        var (appIcon, customIcon) = LoadAppIcon();
        _customIcon = customIcon;

        _notifyIcon = new NotifyIcon
        {
            Icon = appIcon,
            Text = "AudioMirror — WASAPI Stream Cloner",
            ContextMenuStrip = contextMenu,
            Visible = true
        };

        _notifyIcon.DoubleClick += (_, _) => OpenRequested?.Invoke();
    }

    private static (Icon icon, Icon? customToDispose) LoadAppIcon()
    {
        try
        {
            var assetsIcon = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
            if (File.Exists(assetsIcon))
            {
                var custom = new Icon(assetsIcon);
                return (custom, custom);
            }

            var exePath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
            {
                var extracted = Icon.ExtractAssociatedIcon(exePath);
                if (extracted != null)
                {
                    return (extracted, extracted);
                }
            }
        }
        catch
        {
            // Fallback en caso de error de acceso a archivos
        }

        return (SystemIcons.Application, null);
    }

    public void UpdateStatus(bool isRunning, string currentSource)
    {
        if (_disposed) return;

        _toggleBridgeMenuItem.Text = isRunning ? "Detener Duplicación" : "Iniciar Duplicación";
        _statusMenuItem.Text = isRunning ? $"Activo: {currentSource}" : "Estado: Detenido";
        _notifyIcon.Text = isRunning ? $"AudioMirror ({currentSource})" : "AudioMirror (Detenido)";
    }

    public void UpdateHotKeyText(string hotKeyDisplay)
    {
        if (_disposed) return;
        _nextSourceMenuItem.Text = $"Siguiente fuente de audio ({hotKeyDisplay})";
    }

    public void ShowNotification(string title, string message, ToolTipIcon icon = ToolTipIcon.Info)
    {
        if (_disposed) return;
        _notifyIcon.ShowBalloonTip(3000, title, message, icon);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _customIcon?.Dispose();
            _disposed = true;
        }
    }
}
