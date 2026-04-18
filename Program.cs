using System.Threading;
using System.Windows.Forms;

namespace RefreshToggle;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
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

            if (StartupManager.HasEntry())
            {
                StartupManager.Enable();
            }
        }
        catch (Exception ex)
        {
            installResult = new InstallResult(false);
            installError = $"Could not install to {InstallationManager.InstallDirectory}: {ex.Message}";
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        using var trayApp = new TrayApp(installResult.InstalledNow, installError);
        Application.Run();
    }
}
