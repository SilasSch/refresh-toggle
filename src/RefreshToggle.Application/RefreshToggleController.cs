using RefreshToggle.Core;

namespace RefreshToggle.Application;

internal sealed class AppStateChangedEventArgs(AppState state) : EventArgs
{
    public AppState State { get; } = state ?? throw new ArgumentNullException(nameof(state));
}

internal sealed class RefreshToggleController : IAsyncDisposable
{
    public static readonly TimeSpan DisplayEventDebounce = TimeSpan.FromMilliseconds(500);

    private readonly IConfigStore _configStore;
    private readonly IDisplayService _displayService;
    private readonly IStartupService _startupService;
    private readonly IInstallationService _installationService;
    private readonly IHotkeyService _hotkeyService;
    private readonly HotkeyChangeTransaction _hotkeyTransaction;
    private readonly SerializedCommandGate _commands;
    private readonly TrailingDebouncer _displayDebouncer;
    private AppState _state = new();
    private HotkeyActivation? _activeHotkey;
    private int _disposeStarted;

    public RefreshToggleController(
        IConfigStore configStore,
        IDisplayService displayService,
        IStartupService startupService,
        IInstallationService installationService,
        IHotkeyService hotkeyService,
        IDelayScheduler? delayScheduler = null)
    {
        _configStore = configStore ?? throw new ArgumentNullException(nameof(configStore));
        _displayService = displayService ?? throw new ArgumentNullException(nameof(displayService));
        _startupService = startupService ?? throw new ArgumentNullException(nameof(startupService));
        _installationService = installationService ?? throw new ArgumentNullException(nameof(installationService));
        _hotkeyService = hotkeyService ?? throw new ArgumentNullException(nameof(hotkeyService));
        _hotkeyTransaction = new HotkeyChangeTransaction(configStore, hotkeyService);
        _commands = new SerializedCommandGate();
        _displayDebouncer = new TrailingDebouncer(DisplayEventDebounce, delayScheduler);
        _hotkeyService.HotkeyPressed += OnHotkeyPressed;
    }

    public event EventHandler<AppStateChangedEventArgs>? StateChanged;

    public AppState State => Volatile.Read(ref _state);

    public Task<Result<AppState>> InitializeAsync(CancellationToken cancellationToken = default) =>
        ExecuteCommandAsync(InitializeCoreAsync, cancellationToken);

    public Task<Result<AppState>> RefreshDisplaysAsync(CancellationToken cancellationToken = default) =>
        ExecuteCommandAsync(RefreshDisplaysCoreAsync, cancellationToken);

    public Task<Result<AppState>> ToggleAsync(CancellationToken cancellationToken = default) =>
        ExecuteCommandAsync(ToggleCoreAsync, cancellationToken);

    public Task<Result<AppState>> ChangeRatesAsync(
        RatePair rates,
        CancellationToken cancellationToken = default) =>
        ExecuteCommandAsync(token => ChangeRatesCoreAsync(rates, token), cancellationToken);

    public Task<Result<AppState>> ChangeHotkeyAsync(
        HotkeyBinding binding,
        CancellationToken cancellationToken = default) =>
        ExecuteCommandAsync(token => ChangeHotkeyCoreAsync(binding, token), cancellationToken);

    public Task<Result<AppState>> SetStartupEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken = default) =>
        ExecuteCommandAsync(token => SetStartupEnabledCoreAsync(enabled, token), cancellationToken);

    public Task<Result<AppState>> EnsureInstalledAsync(CancellationToken cancellationToken = default) =>
        ExecuteCommandAsync(EnsureInstalledCoreAsync, cancellationToken);

    public Task<Result<AppState>> BeginUninstallAsync(CancellationToken cancellationToken = default) =>
        ExecuteCommandAsync(BeginUninstallCoreAsync, cancellationToken);

    public Task QueueDisplayRefreshAsync() =>
        _displayDebouncer.SignalAsync(async cancellationToken =>
        {
            await RefreshDisplaysAsync(cancellationToken).ConfigureAwait(false);
        });

    private async Task<Result> InitializeCoreAsync(CancellationToken cancellationToken)
    {
        if (State.IsInitialized)
        {
            return Result.Success();
        }

        var errors = new List<AppError>();
        string? notification = null;

        var loadConfig = await _configStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        var config = AppConfig.Default;
        if (loadConfig.IsSuccess)
        {
            config = loadConfig.Value.Config;
            if (loadConfig.Value.Warning is not null)
            {
                errors.Add(loadConfig.Value.Warning);
            }

            if (loadConfig.Value.WasReset)
            {
                notification = "Configuration was reset to safe defaults.";
            }
            else if (loadConfig.Value.NeedsMigration)
            {
                notification = "Configuration was migrated to schema version 1.";
            }
        }
        else
        {
            errors.Add(loadConfig.Error!);
        }

        var configValidation = config.Validate();
        if (configValidation.IsFailure)
        {
            errors.Add(configValidation.Error!);
            config = AppConfig.Default;
            var saveDefaults = await _configStore.SaveAsync(config, cancellationToken).ConfigureAwait(false);
            if (saveDefaults.IsFailure)
            {
                errors.Add(saveDefaults.Error!);
            }
        }

        var installation = await _installationService.GetStateAsync(cancellationToken).ConfigureAwait(false);
        var installationState = installation.IsSuccess
            ? installation.Value
            : new InstallationState(
                DeploymentMode.DevelopmentPortable,
                IsInstalled: false,
                CanManageInstallation: false,
                Detail: installation.Error!.Message);
        if (installation.IsFailure)
        {
            errors.Add(installation.Error!);
        }

        var startup = await _startupService.GetStateAsync(cancellationToken).ConfigureAwait(false);
        if (startup.IsFailure)
        {
            errors.Add(startup.Error!);
        }

        var displays = await _displayService.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        if (displays.IsFailure)
        {
            errors.Add(displays.Error!);
        }

        var hotkey = await _hotkeyTransaction
            .RegisterInitialAsync(config, cancellationToken)
            .ConfigureAwait(false);
        if (hotkey.IsSuccess)
        {
            _activeHotkey = hotkey.Value;
        }
        else
        {
            errors.Add(hotkey.Error!);
        }

        Publish(new AppState
        {
            Config = config,
            Displays = displays.IsSuccess ? displays.Value : DisplaySnapshot.Empty,
            StartupEnabled = startup.IsSuccess && startup.Value.IsEnabled,
            StartupAvailable = installationState.CanManageInstallation && startup.IsSuccess,
            IsDistributionBuild = installationState.IsDistributionBuild,
            IsInstalled = installationState.IsInstalled,
            CanManageInstallation = installationState.CanManageInstallation,
            HotkeyRegistered = hotkey.IsSuccess,
            IsBusy = true,
            IsInitialized = true,
            LastError = Combine(errors),
            Notification = notification ?? installationState.Detail
        });

        return Result.Success();
    }

    private async Task<Result> RefreshDisplaysCoreAsync(CancellationToken cancellationToken)
    {
        var snapshot = await _displayService.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        if (snapshot.IsFailure)
        {
            return Result.Failure(snapshot.Error!);
        }

        Publish(State with { Displays = snapshot.Value, LastError = null });
        return Result.Success();
    }

    private async Task<Result> ToggleCoreAsync(CancellationToken cancellationToken)
    {
        var snapshot = await _displayService.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        if (snapshot.IsFailure)
        {
            return Result.Failure(snapshot.Error!);
        }

        Publish(State with { Displays = snapshot.Value });

        var rates = State.Config.GetRatePair();
        if (rates.IsFailure)
        {
            return Result.Failure(rates.Error!);
        }

        var plan = RefreshRatePolicy.CreateTogglePlan(snapshot.Value, rates.Value);
        if (plan.IsFailure)
        {
            return Result.Failure(plan.Error!);
        }

        var apply = await _displayService.ApplyAsync(plan.Value, cancellationToken).ConfigureAwait(false);
        if (apply.IsFailure)
        {
            return Result.Failure(apply.Error!);
        }

        Publish(State with
        {
            Displays = apply.Value,
            LastError = null,
            Notification = $"Refresh rate changed to {plan.Value.TargetRate} Hz."
        });
        return Result.Success();
    }

    private async Task<Result> ChangeRatesCoreAsync(RatePair rates, CancellationToken cancellationToken)
    {
        var proposedConfig = State.Config.WithRates(rates);
        var save = await _configStore.SaveAsync(proposedConfig, cancellationToken).ConfigureAwait(false);
        if (save.IsFailure)
        {
            return Result.Failure(save.Error!);
        }

        Publish(State with { Config = proposedConfig, LastError = null });
        return Result.Success();
    }

    private async Task<Result> ChangeHotkeyCoreAsync(
        HotkeyBinding binding,
        CancellationToken cancellationToken)
    {
        Result<HotkeyActivation> change;
        if (_activeHotkey is null)
        {
            change = await _hotkeyTransaction
                .RegisterAndSaveAsync(State.Config, binding, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            change = await _hotkeyTransaction
                .ChangeAsync(_activeHotkey, binding, cancellationToken)
                .ConfigureAwait(false);
        }

        if (change.IsFailure)
        {
            return Result.Failure(change.Error!);
        }

        _activeHotkey = change.Value;
        Publish(State with
        {
            Config = change.Value.Config,
            HotkeyRegistered = true,
            LastError = null,
            Notification = $"Hotkey changed to {binding.DisplayText}."
        });
        return Result.Success();
    }

    private async Task<Result> SetStartupEnabledCoreAsync(
        bool enabled,
        CancellationToken cancellationToken)
    {
        if (!State.StartupAvailable)
        {
            return Result.Failure(
                new AppError(ErrorCode.Unavailable, "Startup management is unavailable in portable mode."));
        }

        var startup = await _startupService
            .SetEnabledAsync(enabled, cancellationToken)
            .ConfigureAwait(false);
        if (startup.IsFailure)
        {
            return Result.Failure(startup.Error!);
        }

        Publish(State with { StartupEnabled = startup.Value.IsEnabled, LastError = null });
        return Result.Success();
    }

    private async Task<Result> EnsureInstalledCoreAsync(CancellationToken cancellationToken)
    {
        var install = await _installationService
            .EnsureInstalledAsync(cancellationToken)
            .ConfigureAwait(false);
        if (install.IsFailure)
        {
            return Result.Failure(install.Error!);
        }

        PublishInstallation(install.Value);
        return Result.Success();
    }

    private async Task<Result> BeginUninstallCoreAsync(CancellationToken cancellationToken)
    {
        if (!State.CanManageInstallation)
        {
            return Result.Failure(
                new AppError(ErrorCode.Unavailable, "Uninstall is unavailable in portable mode."));
        }

        var uninstall = await _installationService
            .BeginUninstallAsync(cancellationToken)
            .ConfigureAwait(false);
        if (uninstall.IsFailure)
        {
            return Result.Failure(uninstall.Error!);
        }

        PublishInstallation(uninstall.Value);
        return Result.Success();
    }

    private void PublishInstallation(InstallationResult result)
    {
        Publish(State with
        {
            IsDistributionBuild = result.State.IsDistributionBuild,
            IsInstalled = result.State.IsInstalled,
            CanManageInstallation = result.State.CanManageInstallation,
            StartupAvailable = result.State.CanManageInstallation,
            ExitRequested = result.ExitRequired,
            Notification = result.Notification ?? result.State.Detail,
            LastError = null
        });
    }

    private Task<Result<AppState>> ExecuteCommandAsync(
        Func<CancellationToken, Task<Result>> command,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeStarted) != 0, this);

        return _commands.ExecuteAsync(
            async token =>
            {
                Publish(State with { IsBusy = true, LastError = null });

                Result result;
                try
                {
                    result = await command(token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    result = Result.Failure(AppError.Cancelled());
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    result = Result.Failure(
                        new AppError(ErrorCode.Unknown, "The command failed unexpectedly.", exception));
                }

                if (result.IsFailure)
                {
                    Publish(State with { LastError = result.Error });
                }

                Publish(State with { IsBusy = false });
                return result.IsSuccess
                    ? Result.Success(State)
                    : Result.Failure<AppState>(result.Error!);
            },
            cancellationToken);
    }

    private void OnHotkeyPressed(object? sender, EventArgs eventArgs)
    {
        if (Volatile.Read(ref _disposeStarted) == 0)
        {
            _ = ToggleAsync();
        }
    }

    private void Publish(AppState state)
    {
        Volatile.Write(ref _state, state);
        var handlers = StateChanged;
        if (handlers is null)
        {
            return;
        }

        var eventArgs = new AppStateChangedEventArgs(state);
        foreach (EventHandler<AppStateChangedEventArgs> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, eventArgs);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // A view subscriber must not compromise application state transitions.
            }
        }
    }

    private static AppError? Combine(List<AppError> errors)
    {
        if (errors.Count == 0)
        {
            return null;
        }

        return new AppError(
            errors[0].Code,
            string.Join(" ", errors.Select(error => error.Message)),
            errors[0].Exception);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
        {
            return;
        }

        _hotkeyService.HotkeyPressed -= OnHotkeyPressed;
        await _displayDebouncer.DisposeAsync();
        await _commands.DisposeAsync();

        if (_activeHotkey is not null)
        {
            await _hotkeyService
                .UnregisterAsync(_activeHotkey.Registration, CancellationToken.None);
            _activeHotkey = null;
        }

        await _hotkeyService.DisposeAsync();
    }
}
