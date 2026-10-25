using System.Windows;
using AudioMirror.Core;

namespace AudioMirror;

public partial class App : System.Windows.Application
{
    private readonly SettingsService _settingsService = new();

    public static bool IsSessionEnding { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Prevenir cierre automático si la ventana principal arranca oculta
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        OsdNotificationManager.Initialize();

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

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        base.OnSessionEnding(e);
        // Permitir que Windows se reinicie o apague sin bloquearlo
        IsSessionEnding = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        OsdNotificationManager.Close();
        base.OnExit(e);
    }
}