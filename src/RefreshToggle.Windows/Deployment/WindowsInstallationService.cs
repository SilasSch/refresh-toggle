using RefreshToggle.Application;
using RefreshToggle.Core;
using RefreshToggle.Windows.Startup;

namespace RefreshToggle.Windows.Deployment;

internal sealed class WindowsInstallationService : IInstallationService
{
    private readonly DeploymentManifest _deployment;
    private readonly InstallationPaths _paths;
    private readonly AtomicInstaller _installer;
    private readonly CleanupLauncher _cleanupLauncher;
    private readonly RegistryStartupStoreEngine _startupStore;

    public WindowsInstallationService(
        DeploymentManifest deployment,
        InstallationPaths paths,
        RegistryStartupStoreEngine startupStore,
        AtomicInstaller? installer = null,
        CleanupLauncher? cleanupLauncher = null)
    {
        ArgumentNullException.ThrowIfNull(deployment);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(startupStore);

        _deployment = deployment;
        _paths = paths;
        _startupStore = startupStore;
        _installer = installer ?? new AtomicInstaller(paths);
        _cleanupLauncher = cleanupLauncher ?? new CleanupLauncher();
    }

    public Task<Result<InstallationState>> GetStateAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(Result.Failure<InstallationState>(AppError.Cancelled()));
        }

        if (_deployment.MayMutateInstallation)
        {
            StaleCleanup.RemoveRecognizedArtifacts();
        }
        return Task.FromResult(Result.Success(CreateState()));
    }

    public async Task<Result<InstallationResult>> EnsureInstalledAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_deployment.MayMutateInstallation)
        {
            return Result.Success(new InstallationResult(
                CreateState(),
                InstalledNow: false,
                ExitRequired: false,
                _deployment.Reason ?? "Running in portable development mode."));
        }

        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
        {
            return Result.Failure<InstallationResult>(new AppError(
                ErrorCode.Installation,
                "The current executable path could not be determined."));
        }

        var installation = await _installer
            .EnsureInstalledAsync(processPath, cancellationToken)
            .ConfigureAwait(false);
        if (installation.IsFailure)
        {
            return Result.Failure<InstallationResult>(installation.Error!);
        }

        var notification = installation.Value.InstalledNow
            ? @"Installed safely to %LOCALAPPDATA%\RefreshToggle."
            : null;
        return Result.Success(new InstallationResult(
            CreateState(),
            installation.Value.InstalledNow,
            ExitRequired: false,
            notification));
    }

    public async Task<Result<InstallationResult>> BeginUninstallAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_deployment.MayMutateInstallation)
        {
            return Result.Failure<InstallationResult>(new AppError(
                ErrorCode.Unavailable,
                _deployment.Reason ?? "Uninstall is unavailable in portable mode."));
        }

        if (!File.Exists(_paths.InstalledExecutablePath))
        {
            return Result.Success(new InstallationResult(
                CreateState(),
                InstalledNow: false,
                ExitRequired: false,
                "No installed executable was found. User configuration was retained."));
        }

        var startupState = _startupStore.GetState();
        if (startupState.IsFailure)
        {
            return Result.Failure<InstallationResult>(startupState.Error!);
        }

        var currentPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(currentPath) && PathsEqual(currentPath, _paths.InstalledExecutablePath))
        {
            return await BeginSelfUninstallAsync(
                    currentPath,
                    startupState.Value.IsEnabled,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var disable = _startupStore.DisableForValidatedCleanup();
        if (disable.IsFailure)
        {
            return Result.Failure<InstallationResult>(disable.Error!);
        }

        try
        {
            File.Delete(_paths.InstalledExecutablePath);
            TryDeleteEmptyInstallDirectory();
        }
        catch (Exception exception) when (IsInstallationException(exception))
        {
            var rollback = startupState.Value.IsEnabled ? _startupStore.Enable() : Result.Success();
            return Result.Failure<InstallationResult>(AppError.Combine(
                new AppError(
                    ErrorCode.Installation,
                    $"The installed executable could not be removed: {exception.Message}",
                    exception),
                rollback.Error));
        }

        return Result.Success(new InstallationResult(
            CreateState(),
            InstalledNow: false,
            ExitRequired: false,
            "Installed executable and startup entry were removed. User configuration was retained."));
    }

    private async Task<Result<InstallationResult>> BeginSelfUninstallAsync(
        string currentPath,
        bool startupWasEnabled,
        CancellationToken cancellationToken)
    {
        var launch = await _cleanupLauncher.StartAsync(currentPath, cancellationToken).ConfigureAwait(false);
        if (launch.IsFailure)
        {
            return Result.Failure<InstallationResult>(launch.Error!);
        }

        using var session = launch.Value;
        var disable = _startupStore.DisableForValidatedCleanup();
        if (disable.IsFailure)
        {
            return Result.Failure<InstallationResult>(disable.Error!);
        }

        var commit = session.Commit();
        if (commit.IsFailure)
        {
            var rollback = startupWasEnabled ? _startupStore.Enable() : Result.Success();
            return Result.Failure<InstallationResult>(
                AppError.Combine(commit.Error!, rollback.Error));
        }

        return Result.Success(new InstallationResult(
            CreateState(),
            InstalledNow: false,
            ExitRequired: true,
            "Uninstall will be completed after RefreshToggle exits. User configuration will be retained."));
    }

    private InstallationState CreateState()
    {
        var mode = _deployment.MayMutateInstallation
            ? DeploymentMode.Distribution
            : DeploymentMode.DevelopmentPortable;
        return new InstallationState(
            mode,
            File.Exists(_paths.InstalledExecutablePath),
            _deployment.MayMutateInstallation,
            _deployment.Reason);
    }

    private void TryDeleteEmptyInstallDirectory()
    {
        if (Directory.Exists(_paths.InstallDirectory) &&
            (File.GetAttributes(_paths.InstallDirectory) & FileAttributes.ReparsePoint) == 0 &&
            !Directory.EnumerateFileSystemEntries(_paths.InstallDirectory).Any())
        {
            Directory.Delete(_paths.InstallDirectory, recursive: false);
        }
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            StringComparison.OrdinalIgnoreCase);

    private static bool IsInstallationException(Exception exception) => exception is
        IOException or
        UnauthorizedAccessException or
        System.Security.SecurityException or
        ArgumentException or
        NotSupportedException;
}
