using AiQuotaTray.Logging;
using Serilog;

namespace AiQuotaTray;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        AppLog.Initialize();
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Log.Fatal(e.ExceptionObject as Exception, "Unhandled exception (terminating: {IsTerminating})", e.IsTerminating);
        Application.ThreadException += (_, e) => Log.Error(e.Exception, "Unhandled UI thread exception");

        try
        {
            using var singleInstance = new Mutex(initiallyOwned: true, "Global\\AiQuotaTray-SingleInstance", out var createdNew);
            if (!createdNew)
            {
                MessageBox.Show("AI Quota Tray is already running.", "AI Quota Tray", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Log.Information("AiQuotaTray starting");
            ApplicationConfiguration.Initialize();
            Application.Run(new TrayApplicationContext());
        }
        finally
        {
            Log.Information("AiQuotaTray exiting");
            Log.CloseAndFlush();
        }
    }
}
