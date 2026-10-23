using System.Windows;
using AudioMirror.Core;

namespace AudioMirror;

public partial class App : System.Windows.Application
{
    private readonly SettingsService _settingsService = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Prevenir cierre automático si la ventana principal arranca oculta
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var settings = _settingsService.Load();
        bool hasMinimizedArg = e.Args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));
        bool startMinimized = hasMinimizedArg || settings.StartMinimized;

        var mainWindow = new MainWindow(startMinimized, settings);
        MainWindow = mainWindow;

        if (!startMinimized)
        {
            mainWindow.Show();
        }
    }
}
