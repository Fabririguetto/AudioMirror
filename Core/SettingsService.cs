using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using AudioMirror.Models;

namespace AudioMirror.Core;

/// <summary>
/// Gestiona la lectura y escritura persistente y segura de la configuración en %AppData%/AudioMirror/config.json.
/// </summary>
public sealed class SettingsService
{
    private static readonly object _fileLock = new();

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public string ConfigDirectory { get; }
    public string ConfigFilePath { get; }

    public SettingsService()
    {
        ConfigDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AudioMirror"
        );
        ConfigFilePath = Path.Combine(ConfigDirectory, "config.json");
    }

    /// <summary>
    /// Carga la configuración desde el disco o retorna valores por defecto en caso de ausencia o fallo.
    /// </summary>
    public AppSettings Load()
    {
        lock (_fileLock)
        {
            try
            {
                if (!File.Exists(ConfigFilePath))
                {
                    var defaultSettings = new AppSettings();
                    Save(defaultSettings);
                    return defaultSettings;
                }

                string json = File.ReadAllText(ConfigFilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions);
                return settings ?? new AppSettings();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SettingsService] Error al leer config.json ({ex.Message}). Creando respaldo y restaurando defaults.");
                try
                {
                    if (File.Exists(ConfigFilePath))
                    {
                        string backupPath = Path.Combine(ConfigDirectory, $"config.corrupted.{DateTime.UtcNow:yyyyMMddHHmmss}.bak");
                        File.Move(ConfigFilePath, backupPath, overwrite: true);
                    }
                }
                catch
                {
                }

                var fallbackSettings = new AppSettings();
                Save(fallbackSettings);
                return fallbackSettings;
            }
        }
    }

    /// <summary>
    /// Guarda la configuración en disco de forma atómica y segura contra cortes imprevistos.
    /// </summary>
    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        lock (_fileLock)
        {
            try
            {
                if (!Directory.Exists(ConfigDirectory))
                {
                    Directory.CreateDirectory(ConfigDirectory);
                }

                string json = JsonSerializer.Serialize(settings, _jsonOptions);

                // Escritura atómica vía archivo temporal
                string tempFilePath = Path.Combine(ConfigDirectory, $"config.{Guid.NewGuid():N}.tmp");
                File.WriteAllText(tempFilePath, json);
                File.Move(tempFilePath, ConfigFilePath, overwrite: true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SettingsService] Fallo al guardar configuración: {ex.Message}");
            }
        }
    }
}
