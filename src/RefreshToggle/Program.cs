namespace RefreshToggle;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Same mutex name as v0.4.0, so old and new builds never run side by side.
        using var mutex = new Mutex(initiallyOwned: true, @"Local\RefreshToggle.SingleInstance", out var isFirstInstance);
        if (!isFirstInstance)
        {
            return;
        }

        ApplicationConfiguration.Initialize();
        using var app = new TrayApp(); // disposing removes the tray icon right away on exit
        Application.Run(app);
    }
}
