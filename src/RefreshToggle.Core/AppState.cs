namespace RefreshToggle.Core;

internal sealed record AppState
{
    public AppConfig Config { get; init; } = AppConfig.Default;

    public DisplaySnapshot Displays { get; init; } = DisplaySnapshot.Empty;

    public bool StartupEnabled { get; init; }

    public bool StartupAvailable { get; init; }

    public bool IsDistributionBuild { get; init; }

    public bool IsInstalled { get; init; }

    public bool CanManageInstallation { get; init; }

    public bool HotkeyRegistered { get; init; }

    public bool IsBusy { get; init; }

    public bool IsInitialized { get; init; }

    public bool ExitRequested { get; init; }

    public AppError? LastError { get; init; }

    public string? Notification { get; init; }
}
