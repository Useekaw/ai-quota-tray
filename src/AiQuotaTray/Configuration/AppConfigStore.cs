using System.Text.Json;
using Serilog;

namespace AiQuotaTray.Configuration;

/// <summary>Reads/writes the user-editable JSON config file at %LocalAppData%\AiQuotaTray\config.json.</summary>
internal static class AppConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public static string ConfigPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AiQuotaTray",
        "config.json");

    /// <summary>
    /// Loads the config file, writing it with defaults on first run so
    /// there's something to edit. A missing/malformed file falls back to
    /// defaults for this run rather than overwriting whatever's on disk —
    /// a typo shouldn't silently erase the user's settings; the load error
    /// is returned so the caller can log it once the logger is up.
    /// </summary>
    public static (AppConfig Config, string? LoadError) Load()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);

            if (!File.Exists(ConfigPath))
            {
                File.WriteAllText(ConfigPath, JsonSerializer.Serialize(AppConfig.Default, JsonOptions));
                return (AppConfig.Default, null);
            }

            var config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigPath), JsonOptions);
            return config is null
                ? (AppConfig.Default, $"{ConfigPath} deserialized to null; using defaults.")
                : (config.Sanitized(), null);
        }
        catch (Exception ex)
        {
            return (AppConfig.Default, $"Failed to load {ConfigPath}: {ex.Message}. Using defaults.");
        }
    }

    public static void OpenConfigFile()
    {
        try
        {
            if (!File.Exists(ConfigPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
                File.WriteAllText(ConfigPath, JsonSerializer.Serialize(AppConfig.Default, JsonOptions));
            }
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = ConfigPath,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to open config file {ConfigPath}", ConfigPath);
        }
    }
}
