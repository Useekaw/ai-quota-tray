using Serilog;
using Serilog.Events;

namespace AiQuotaTray.Logging;

/// <summary>Bootstraps the Serilog file logger and exposes where its output lives.</summary>
internal static class AppLog
{
    public static string LogDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AiQuotaTray",
        "logs");

    /// <param name="minimumLevel">One of Serilog's <see cref="LogEventLevel"/> names (Verbose/Debug/Information/Warning/Error/Fatal); an unrecognized value falls back to Information.</param>
    public static void Initialize(string minimumLevel = "Information")
    {
        Directory.CreateDirectory(LogDirectory);

        var level = Enum.TryParse<LogEventLevel>(minimumLevel, ignoreCase: true, out var parsed)
            ? parsed
            : LogEventLevel.Information;

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(level)
            .Enrich.FromLogContext()
            .WriteTo.File(
                Path.Combine(LogDirectory, "diagnostics-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                shared: true,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }

    public static void OpenLogFolder()
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = LogDirectory,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to open log folder {LogDirectory}", LogDirectory);
        }
    }
}
