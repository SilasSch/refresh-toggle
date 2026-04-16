using Microsoft.Win32;

namespace RefreshToggle;

internal static class StartupManager
{
    private const string RegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "RefreshToggle";

    public static void Enable()
    {
        var exePath = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot determine executable path.");
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
        var exePath = Environment.ProcessPath;
        var normalizedStoredValue = storedValue.Trim().Trim('"');
        return exePath is not null
            && string.Equals(normalizedStoredValue, exePath, StringComparison.OrdinalIgnoreCase);
    }
}
