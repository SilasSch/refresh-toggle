using System.Threading;
using System.Windows.Forms;

namespace RefreshToggle;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length > 0 && string.Equals(args[0], "--cleanup", StringComparison.Ordinal))
        {
            InstallationManager.RunCleanupMode(args[1..]);
            return;
        }

        using var mutex = new Mutex(true, @"Global\RefreshToggle_SingleInstance", out var createdNew);
        if (!createdNew)
        {
            return;
        }

        InstallResult installResult;
        string? installError = null;
        try
        {
            installResult = InstallationManager.EnsureInstalled();
        }
        catch (Exception ex)
        {
            installResult = new InstallResult(false);
            installError = $"Could not install to {InstallationManager.InstallDirectory}: {ex.Message}";
        }

        string? startupMigrationError = null;
        if (StartupManager.HasEntry())
        {
            try
            {
                StartupManager.Enable();
            }
            catch (Exception ex)
            {
                startupMigrationError = $"Could not update startup entry: {ex.Message}";
            }
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        using var trayApp = new TrayApp(installResult.InstalledNow, installError, startupMigrationError);
        Application.Run();
    }
}
