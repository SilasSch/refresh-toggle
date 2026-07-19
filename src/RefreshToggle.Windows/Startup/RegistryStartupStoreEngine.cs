using Microsoft.Win32;
using RefreshToggle.Core;
using RefreshToggle.Windows.Deployment;

namespace RefreshToggle.Windows.Startup;

internal sealed record StartupRegistryState(bool IsEnabled, bool HasEntry, string? StoredCommand);

/// <summary>
/// Owns the single source of truth for start-with-Windows. No startup state is
/// inferred from, or written to, the JSON configuration.
/// </summary>
internal sealed class RegistryStartupStoreEngine
{
    public const string DefaultRegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string DefaultValueName = "RefreshToggle";

    private readonly InstallationPaths _paths;
    private readonly DeploymentManifest _deployment;
    private readonly string _registryKeyPath;
    private readonly string _valueName;

    public RegistryStartupStoreEngine(
        InstallationPaths paths,
        DeploymentManifest deployment,
        string registryKeyPath = DefaultRegistryKeyPath,
        string valueName = DefaultValueName)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(deployment);
        ArgumentException.ThrowIfNullOrWhiteSpace(registryKeyPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(valueName);

        _paths = paths;
        _deployment = deployment;
        _registryKeyPath = registryKeyPath;
        _valueName = valueName;
    }

    public Result<StartupRegistryState> GetState()
    {
        if (!OperatingSystem.IsWindows())
        {
            return Result.Failure<StartupRegistryState>(new AppError(
                ErrorCode.Unavailable,
                "Windows startup registration is only available on Windows."));
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(_registryKeyPath, writable: false);
            var storedCommand = key?.GetValue(
                _valueName,
                defaultValue: null,
                RegistryValueOptions.DoNotExpandEnvironmentNames) as string;

            if (storedCommand is null)
            {
                return Result.Success(new StartupRegistryState(
                    IsEnabled: false,
                    HasEntry: false,
                    StoredCommand: null));
            }

            var isEnabled = File.Exists(_paths.InstalledExecutablePath) &&
                            TryExtractExecutablePath(storedCommand, out var executablePath) &&
                            PathsEqual(executablePath, _paths.InstalledExecutablePath);

            return Result.Success(new StartupRegistryState(
                isEnabled,
                HasEntry: true,
                storedCommand));
        }
        catch (Exception exception) when (IsRegistryException(exception))
        {
            return Result.Failure<StartupRegistryState>(new AppError(
                ErrorCode.Persistence,
                $"The startup registry entry could not be read: {exception.Message}",
                exception));
        }
    }

    public Result Enable()
    {
        var guard = ValidateMutationAllowed();
        if (guard.IsFailure)
        {
            return guard;
        }

        if (!File.Exists(_paths.InstalledExecutablePath))
        {
            return Result.Failure(new AppError(
                ErrorCode.NotFound,
                $"The installed executable was not found: '{_paths.InstalledExecutablePath}'."));
        }

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(_registryKeyPath, writable: true);
            if (key is null)
            {
                return Result.Failure(new AppError(
                    ErrorCode.Persistence,
                    $"The startup registry key could not be created: '{_registryKeyPath}'."));
            }

            key.SetValue(
                _valueName,
                QuoteExecutablePath(_paths.InstalledExecutablePath),
                RegistryValueKind.String);
            key.Flush();
            return Result.Success();
        }
        catch (Exception exception) when (IsRegistryException(exception))
        {
            return Result.Failure(new AppError(
                ErrorCode.Persistence,
                $"The startup registry entry could not be enabled: {exception.Message}",
                exception));
        }
    }

    public Result Disable() => DisableCore(requireDistribution: true);

    /// <summary>
    /// Used only by the validated distribution cleanup path after the helper is
    /// ready. Keeping this separate prevents portable builds from silently mutating
    /// the Run key while still allowing an already installed release to uninstall.
    /// </summary>
    public Result DisableForValidatedCleanup() => DisableCore(requireDistribution: false);

    private Result DisableCore(bool requireDistribution)
    {
        if (requireDistribution)
        {
            var guard = ValidateMutationAllowed();
            if (guard.IsFailure)
            {
                return guard;
            }
        }

        if (!OperatingSystem.IsWindows())
        {
            return Result.Failure(new AppError(ErrorCode.Unavailable, "Startup is unavailable on this platform."));
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(_registryKeyPath, writable: true);
            key?.DeleteValue(_valueName, throwOnMissingValue: false);
            key?.Flush();
            return Result.Success();
        }
        catch (Exception exception) when (IsRegistryException(exception))
        {
            return Result.Failure(new AppError(
                ErrorCode.Persistence,
                $"The startup registry entry could not be disabled: {exception.Message}",
                exception));
        }
    }

    private Result ValidateMutationAllowed()
    {
        if (!_deployment.MayMutateInstallation)
        {
            return Result.Failure(new AppError(
                ErrorCode.Unavailable,
                _deployment.Reason ?? "Startup changes are disabled in portable mode."));
        }

        return OperatingSystem.IsWindows()
            ? Result.Success()
            : Result.Failure(new AppError(ErrorCode.Unavailable, "Startup is unavailable on this platform."));
    }

    private static bool TryExtractExecutablePath(string command, out string executablePath)
    {
        executablePath = string.Empty;
        var trimmed = command.Trim();
        if (trimmed.Length < 3 || trimmed[0] != '"')
        {
            return false;
        }

        var closingQuote = trimmed.IndexOf('"', 1);
        if (closingQuote <= 1 || !string.IsNullOrWhiteSpace(trimmed[(closingQuote + 1)..]))
        {
            return false;
        }

        executablePath = trimmed[1..closingQuote];
        return Path.IsPathFullyQualified(executablePath);
    }

    private static string QuoteExecutablePath(string executablePath) => $"\"{executablePath}\"";

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            StringComparison.OrdinalIgnoreCase);

    private static bool IsRegistryException(Exception exception) => exception is
        UnauthorizedAccessException or
        System.Security.SecurityException or
        IOException or
        ArgumentException;
}
