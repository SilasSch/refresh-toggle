using RefreshToggle.Application;
using RefreshToggle.Windows.Configuration;
using RefreshToggle.Windows.Deployment;
using RefreshToggle.Windows.Displays;
using RefreshToggle.Windows.Hotkeys;
using RefreshToggle.Windows.Startup;

namespace RefreshToggle.App;

internal static class Program
{
    private const string SingleInstanceMutex = @"Local\RefreshToggle.SingleInstance";

    [STAThread]
    private static int Main(string[] args)
    {
        var maintenanceExitCode = TryRunMaintenanceCommand(args);
        if (maintenanceExitCode is not null)
        {
            return maintenanceExitCode.Value;
        }

        using var mutex = new Mutex(
            initiallyOwned: true,
            SingleInstanceMutex,
            out var createdNew);
        if (!createdNew)
        {
            return 0;
        }

        try
        {
            ApplicationConfiguration.Initialize();

            var deployment = DeploymentManifest.InspectCurrentProcess();
            var paths = InstallationPaths.ForCurrentUser();
            var startupStore = new RegistryStartupStoreEngine(paths, deployment);
            var startupService = new RegistryStartupService(startupStore, deployment);
            var installationService = new WindowsInstallationService(
                deployment,
                paths,
                startupStore);

            var configStore = new JsonConfigStore();
            try
            {
                var controller = new RefreshToggleController(
                    configStore,
                    new WindowsDisplayService(),
                    startupService,
                    installationService,
                    new Win32HotkeyService());
                using var applicationContext = new TrayApplicationContext(controller);
                System.Windows.Forms.Application.Run(applicationContext);
            }
            finally
            {
                configStore.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }

            return 0;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            MessageBox.Show(
                $"RefreshToggle could not start.{Environment.NewLine}{Environment.NewLine}{exception.Message}",
                "RefreshToggle",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 1;
        }
    }

    private static int? TryRunMaintenanceCommand(string[] args)
    {
        if (args.Length == 1 && string.Equals(args[0], "--health-check", StringComparison.Ordinal))
        {
            return HealthCheck.Run();
        }

        if (CleanupCommand.IsCommand(args))
        {
            return CleanupCommand.TryParse(args, out var request)
                ? CleanupRunner.RunAsync(request!).GetAwaiter().GetResult()
                : 2;
        }

        if (ReaperCommand.IsCommand(args))
        {
            return ReaperCommand.TryParse(args, out var request)
                ? ReaperRunner.RunAsync(request!).GetAwaiter().GetResult()
                : 2;
        }

        return null;
    }
}
