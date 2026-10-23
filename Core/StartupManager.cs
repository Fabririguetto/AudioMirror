using Microsoft.Win32;

namespace AudioMirror.Core;

/// <summary>
/// Gestiona la entrada en el Registro de Windows para el arranque automático en segundo plano con el sistema.
/// </summary>
public static class StartupManager
{
    private const string RunRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "AudioMirror";

    /// <summary>
    /// Verifica si la aplicación está actualmente registrada para iniciar con Windows.
    /// </summary>
    public static bool IsStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, writable: false);
            if (key == null) return false;

            object? value = key.GetValue(AppName);
            if (value is string command)
            {
                string? processPath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(processPath) && command.Contains(processPath, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StartupManager] Error comprobando inicio automático: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Activa o desactiva el arranque automático con Windows bajo el flag --minimized.
    /// </summary>
    public static bool SetStartup(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, writable: true);
            if (key == null) return false;

            if (enable)
            {
                string? processPath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(processPath)) return false;

                // Registrar ruta entrecomillada con flag --minimized
                string command = $"\"{processPath}\" --minimized";
                key.SetValue(AppName, command, RegistryValueKind.String);
            }
            else
            {
                if (key.GetValue(AppName) != null)
                {
                    key.DeleteValue(AppName, throwOnMissingValue: false);
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StartupManager] Error configurando inicio automático: {ex.Message}");
            return false;
        }
    }
}
