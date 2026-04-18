using Microsoft.Win32;

namespace RefreshToggle;

internal static class StartupManager
{
    private const string RegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "RefreshToggle";

    public static void Enable()
    {
        var exePath = InstallationManager.InstalledExecutablePath;
        if (!File.Exists(exePath))
        {
            throw new InvalidOperationException($"Installed executable was not found: {exePath}");
        }

        using var key = Registry.CurrentUser.CreateSubKey(RegistryKeyPath, writable: true)
            ?? throw new InvalidOperationException($"Cannot create or open registry key: {RegistryKeyPath}");
        key.SetValue(AppName, $"\"{exePath}\"");
    }

    public static void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, writable: true);
        key?.DeleteValue(AppName, throwOnMissingValue: false);
    }

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
        if (key?.GetValue(AppName) is not string storedValue)
            return false;
        var exePath = InstallationManager.InstalledExecutablePath;
        var normalizedStoredValue = storedValue.Trim().Trim('"');
        return string.Equals(normalizedStoredValue, exePath, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns true if a Run entry exists for this app regardless of whether it matches the current executable path.
    /// Use this to detect stale entries left behind when the binary was moved or renamed.
    /// </summary>
    public static bool HasEntry()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
        return key?.GetValue(AppName) is not null;
    }
}
