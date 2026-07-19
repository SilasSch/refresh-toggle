using RefreshToggle.Core;

namespace RefreshToggle.Application;

internal sealed record ConfigLoadResult(
    AppConfig Config,
    bool WasReset,
    bool NeedsMigration,
    AppError? Warning = null);

internal interface IConfigStore
{
    Task<Result<ConfigLoadResult>> LoadAsync(CancellationToken cancellationToken = default);

    Task<Result> SaveAsync(AppConfig config, CancellationToken cancellationToken = default);
}

internal interface IDisplayService
{
    Task<Result<DisplaySnapshot>> GetSnapshotAsync(CancellationToken cancellationToken = default);

    Task<Result<DisplaySnapshot>> ApplyAsync(
        RefreshRatePlan plan,
        CancellationToken cancellationToken = default);
}

internal sealed record StartupState(bool IsEnabled);

internal interface IStartupService
{
    Task<Result<StartupState>> GetStateAsync(CancellationToken cancellationToken = default);

    Task<Result<StartupState>> SetEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken = default);
}

internal enum DeploymentMode
{
    DevelopmentPortable,
    Distribution
}

internal sealed record InstallationState(
    DeploymentMode Mode,
    bool IsInstalled,
    bool CanManageInstallation,
    string? Detail = null)
{
    public bool IsDistributionBuild => Mode == DeploymentMode.Distribution;
}

internal sealed record InstallationResult(
    InstallationState State,
    bool InstalledNow,
    bool ExitRequired,
    string? Notification = null);

internal interface IInstallationService
{
    Task<Result<InstallationState>> GetStateAsync(CancellationToken cancellationToken = default);

    Task<Result<InstallationResult>> EnsureInstalledAsync(CancellationToken cancellationToken = default);

    Task<Result<InstallationResult>> BeginUninstallAsync(CancellationToken cancellationToken = default);
}

internal sealed record HotkeyRegistration(int Slot, HotkeyBinding Binding);

internal interface IHotkeyService : IAsyncDisposable
{
    event EventHandler? HotkeyPressed;

    Task<Result<HotkeyRegistration>> RegisterAsync(
        HotkeyBinding binding,
        CancellationToken cancellationToken = default);

    Task<Result> UnregisterAsync(
        HotkeyRegistration registration,
        CancellationToken cancellationToken = default);
}
