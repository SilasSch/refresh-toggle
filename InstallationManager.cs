using System.Diagnostics;

namespace RefreshToggle;

internal static class InstallationManager
{
    private const string AppDirectoryName = "RefreshToggle";
    private const string AppExecutableName = "RefreshToggle.exe";

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

        Directory.CreateDirectory(InstallDirectory);
        File.Copy(currentPath, InstalledExecutablePath, overwrite: true);
        return new InstallResult(true);
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
        var args =
            $"/c timeout /t 2 /nobreak >nul & del /f /q \"{InstalledExecutablePath}\" & rmdir /q \"{InstallDirectory}\"";

        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = args,
            CreateNoWindow = true,
            UseShellExecute = false
        });
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
