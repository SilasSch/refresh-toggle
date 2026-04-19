using System.Diagnostics;
using System.Globalization;
using System.Threading;

namespace RefreshToggle;

internal static class InstallationManager
{
    private const string AppDirectoryName = "RefreshToggle";
    private const string AppExecutableName = "RefreshToggle.exe";
    private const int MaxDeleteRetries = 20;
    private const int RetryDelayMilliseconds = 250;

    public static string InstallDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppDirectoryName);

    public static string InstalledExecutablePath => Path.Combine(InstallDirectory, AppExecutableName);

    public static bool HasInstalledCopy() => File.Exists(InstalledExecutablePath);

    public static InstallResult EnsureInstalled()
    {
        var currentPath = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot determine executable path.");
        if (PathsEqual(currentPath, InstalledExecutablePath))
        {
            return new InstallResult(false);
        }

        var hadInstalledCopy = File.Exists(InstalledExecutablePath);
        Directory.CreateDirectory(InstallDirectory);
        File.Copy(currentPath, InstalledExecutablePath, overwrite: true);
        return new InstallResult(!hadInstalledCopy);
    }

    public static bool RemoveInstalledCopy()
    {
        if (!HasInstalledCopy())
        {
            return false;
        }

        var currentPath = Environment.ProcessPath;
        if (currentPath is not null && PathsEqual(currentPath, InstalledExecutablePath))
        {
            StartDeferredDeleteProcess();
            return true;
        }

        File.Delete(InstalledExecutablePath);
        TryDeleteInstallDirectory();
        return false;
    }

    private static void StartDeferredDeleteProcess()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = InstalledExecutablePath,
            CreateNoWindow = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("--cleanup");
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add(InstalledExecutablePath);
        startInfo.ArgumentList.Add(InstallDirectory);

        var cleanupProcess = Process.Start(startInfo);

        if (cleanupProcess is null)
        {
            throw new InvalidOperationException("Could not start deferred uninstall process.");
        }

        cleanupProcess.Dispose();
    }

    public static void RunCleanupMode(string[] args)
    {
        if (args.Length != 3)
        {
            return;
        }

        if (!int.TryParse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var processId))
        {
            return;
        }

        var targetExe = args[1];
        var targetDirectory = args[2];
        if (!PathsEqual(targetExe, InstalledExecutablePath) || !PathsEqual(targetDirectory, InstallDirectory))
        {
            return;
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            process.WaitForExit(5000);
        }
        catch
        {
            // Best effort only.
        }

        for (var attempt = 0; attempt < MaxDeleteRetries; attempt++)
        {
            try
            {
                if (File.Exists(targetExe))
                {
                    File.Delete(targetExe);
                }

                break;
            }
            catch
            {
                Thread.Sleep(RetryDelayMilliseconds);
            }
        }

        try
        {
            if (Directory.Exists(targetDirectory))
            {
                Directory.Delete(targetDirectory, recursive: true);
            }
        }
        catch
        {
            // Best effort only.
        }
    }

    private static void TryDeleteInstallDirectory()
    {
        try
        {
            Directory.Delete(InstallDirectory, recursive: false);
        }
        catch
        {
            // Best effort only.
        }
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

}

internal readonly record struct InstallResult(bool InstalledNow);
