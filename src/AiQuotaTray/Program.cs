namespace AiQuotaTray;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var singleInstance = new Mutex(initiallyOwned: true, "Global\\AiQuotaTray-SingleInstance", out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show("AI Quota Tray is already running.", "AI Quota Tray", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext());
    }
}
