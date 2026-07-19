using RefreshToggle.Application;
using RefreshToggle.Core;

namespace RefreshToggle.Tests;

public sealed class RefreshToggleControllerTests
{
    [Fact]
    public async Task InitializeBuildsCompleteStateAndIsIdempotent()
    {
        await using var fixture = await ControllerFixture.CreateAsync();

        Assert.True(fixture.Controller.State.IsInitialized);
        Assert.False(fixture.Controller.State.IsBusy);
        Assert.True(fixture.Controller.State.IsDistributionBuild);
        Assert.True(fixture.Controller.State.IsInstalled);
        Assert.True(fixture.Controller.State.CanManageInstallation);
        Assert.True(fixture.Controller.State.StartupAvailable);
        Assert.True(fixture.Controller.State.HotkeyRegistered);
        Assert.Equal(2, fixture.Controller.State.Displays.Displays.Count);
        Assert.Null(fixture.Controller.State.LastError);

        var loadCalls = fixture.ConfigStore.LoadCalls;
        var snapshotCalls = fixture.Displays.SnapshotCalls;
        var second = await fixture.Controller.InitializeAsync();

        Assert.True(second.IsSuccess);
        Assert.Equal(loadCalls, fixture.ConfigStore.LoadCalls);
        Assert.Equal(snapshotCalls, fixture.Displays.SnapshotCalls);
    }

    [Fact]
    public async Task InitializeKeepsAppUsableWhenEveryOptionalServiceFails()
    {
        var failure = new AppError(ErrorCode.Unavailable, "unavailable");
        var fixture = new ControllerFixture();
        fixture.ConfigStore.LoadResult = Result.Failure<ConfigLoadResult>(failure);
        fixture.Installation.GetResultOverride = Result.Failure<InstallationState>(failure);
        fixture.Startup.GetResult = Result.Failure<StartupState>(failure);
        fixture.Displays.SnapshotHandler = _ =>
            Task.FromResult(Result.Failure<DisplaySnapshot>(failure));
        fixture.Hotkeys.RegisterResults.Enqueue(Result.Failure<HotkeyRegistration>(failure));
        await using var disposableFixture = fixture;

        var result = await fixture.Controller.InitializeAsync();

        Assert.True(result.IsSuccess);
        Assert.True(fixture.Controller.State.IsInitialized);
        Assert.Equal(AppConfig.Default, fixture.Controller.State.Config);
        Assert.Empty(fixture.Controller.State.Displays.Displays);
        Assert.False(fixture.Controller.State.StartupAvailable);
        Assert.False(fixture.Controller.State.HotkeyRegistered);
        Assert.Contains("unavailable", fixture.Controller.State.LastError!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InitializeReplacesInvalidConfigAndReportsFailedRecoverySave()
    {
        var fixture = new ControllerFixture();
        fixture.ConfigStore.LoadResult = Result.Success(new ConfigLoadResult(
            AppConfig.Default with { RateB = AppConfig.Default.RateA },
            false,
            false));
        fixture.ConfigStore.SaveResults.Enqueue(
            Result.Failure(new AppError(ErrorCode.Persistence, "read-only")));
        await using var disposableFixture = fixture;

        var result = await fixture.Controller.InitializeAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(AppConfig.Default, fixture.Controller.State.Config);
        Assert.Single(fixture.ConfigStore.SavedConfigs);
        Assert.Contains("read-only", fixture.Controller.State.LastError!.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, false, "Configuration was reset to safe defaults.")]
    [InlineData(false, true, "Configuration was migrated to schema version 1.")]
    public async Task InitializePublishesConfigRecoveryNotification(
        bool wasReset,
        bool needsMigration,
        string expected)
    {
        var fixture = new ControllerFixture();
        fixture.ConfigStore.LoadResult = Result.Success(
            new ConfigLoadResult(AppConfig.Default, wasReset, needsMigration));
        await using var disposableFixture = fixture;

        await fixture.Controller.InitializeAsync();

        Assert.Equal(expected, fixture.Controller.State.Notification);
    }

    [Fact]
    public async Task InitializeSurfacesNonFatalConfigWarning()
    {
        var fixture = new ControllerFixture();
        fixture.ConfigStore.LoadResult = Result.Success(new ConfigLoadResult(
            AppConfig.Default,
            false,
            false,
            new AppError(ErrorCode.Persistence, "backup failed")));
        await using var disposableFixture = fixture;

        await fixture.Controller.InitializeAsync();

        Assert.Equal("backup failed", fixture.Controller.State.LastError!.Message);
    }

    [Fact]
    public async Task RefreshDisplaysUpdatesSnapshotOrPreservesLastSnapshotOnFailure()
    {
        await using var fixture = await ControllerFixture.CreateAsync();
        fixture.Displays.Snapshot = TestSnapshots.AtRate(120);

        var success = await fixture.Controller.RefreshDisplaysAsync();

        Assert.True(success.IsSuccess);
        Assert.Equal(120, fixture.Controller.State.Displays.PrimaryOrFirst!.CurrentRefreshRate);

        fixture.Displays.SnapshotHandler = _ => Task.FromResult(
            Result.Failure<DisplaySnapshot>(new AppError(ErrorCode.DisplayQuery, "query failed")));
        var failure = await fixture.Controller.RefreshDisplaysAsync();

        Assert.True(failure.IsFailure);
        Assert.Equal(120, fixture.Controller.State.Displays.PrimaryOrFirst!.CurrentRefreshRate);
        Assert.Equal(ErrorCode.DisplayQuery, fixture.Controller.State.LastError!.Code);
    }

    [Fact]
    public async Task ToggleUsesFreshSnapshotAndAppliesPlanToEveryDisplay()
    {
        await using var fixture = await ControllerFixture.CreateAsync();

        var result = await fixture.Controller.ToggleAsync();

        Assert.True(result.IsSuccess);
        var plan = Assert.Single(fixture.Displays.AppliedPlans);
        Assert.Equal(120, plan.TargetRate);
        Assert.Equal(2, plan.Changes.Count);
        Assert.All(plan.Changes, change => Assert.Equal(120, change.TargetRate));
        Assert.Equal(120, fixture.Controller.State.Displays.PrimaryOrFirst!.CurrentRefreshRate);
        Assert.Equal("Refresh rate changed to 120 Hz.", fixture.Controller.State.Notification);
    }

    [Fact]
    public async Task ToggleDoesNotApplyWhenSnapshotQueryOrPolicyFails()
    {
        await using var fixture = await ControllerFixture.CreateAsync();
        fixture.Displays.SnapshotHandler = _ => Task.FromResult(
            Result.Failure<DisplaySnapshot>(new AppError(ErrorCode.DisplayQuery, "query failed")));

        var queryFailure = await fixture.Controller.ToggleAsync();

        Assert.Equal(ErrorCode.DisplayQuery, queryFailure.Error!.Code);
        Assert.Empty(fixture.Displays.AppliedPlans);

        fixture.Displays.SnapshotHandler = null;
        fixture.Displays.Snapshot = new DisplaySnapshot([], DateTimeOffset.UtcNow);
        var policyFailure = await fixture.Controller.ToggleAsync();

        Assert.Equal(ErrorCode.NotFound, policyFailure.Error!.Code);
        Assert.Empty(fixture.Displays.AppliedPlans);
    }

    [Fact]
    public async Task ToggleRejectsMixedMonitorSupportAndReportsApplyFailure()
    {
        await using var fixture = await ControllerFixture.CreateAsync();
        fixture.Displays.Snapshot = new DisplaySnapshot(
            [
                new DisplayState("DISPLAY1", "Primary", true, 60, [60, 120]),
                new DisplayState("DISPLAY2", "Secondary", false, 60, [60])
            ],
            DateTimeOffset.UtcNow);

        var unsupported = await fixture.Controller.ToggleAsync();

        Assert.Equal(ErrorCode.UnsupportedRefreshRate, unsupported.Error!.Code);
        Assert.Empty(fixture.Displays.AppliedPlans);

        fixture.Displays.Snapshot = TestSnapshots.Valid;
        fixture.Displays.ApplyHandler = (_, _) => Task.FromResult(
            Result.Failure<DisplaySnapshot>(new AppError(ErrorCode.DisplayApply, "apply failed")));
        var applyFailure = await fixture.Controller.ToggleAsync();

        Assert.Equal(ErrorCode.DisplayApply, applyFailure.Error!.Code);
        Assert.Single(fixture.Displays.AppliedPlans);
    }

    [Fact]
    public async Task ChangeRatesCommitsOnlyAfterSuccessfulSave()
    {
        await using var fixture = await ControllerFixture.CreateAsync();
        var proposed = RatePair.Create(75, 165).Value;

        var success = await fixture.Controller.ChangeRatesAsync(proposed);

        Assert.True(success.IsSuccess);
        Assert.Equal(proposed, fixture.Controller.State.Config.GetRatePair().Value);

        fixture.ConfigStore.SaveResults.Enqueue(
            Result.Failure(new AppError(ErrorCode.Persistence, "save failed")));
        var rejected = RatePair.Create(90, 144).Value;
        var failure = await fixture.Controller.ChangeRatesAsync(rejected);

        Assert.True(failure.IsFailure);
        Assert.Equal(proposed, fixture.Controller.State.Config.GetRatePair().Value);
    }

    [Fact]
    public async Task ChangeHotkeyCommitsTransactionOrKeepsPreviousBinding()
    {
        await using var fixture = await ControllerFixture.CreateAsync();
        var proposed = HotkeyBinding.Parse("Alt", "F12").Value;

        var success = await fixture.Controller.ChangeHotkeyAsync(proposed);

        Assert.True(success.IsSuccess);
        Assert.Equal(proposed, fixture.Controller.State.Config.GetHotkeyBinding().Value);
        Assert.Equal("Hotkey changed to Alt+F12.", fixture.Controller.State.Notification);

        fixture.Hotkeys.RegisterResults.Enqueue(
            Result.Failure<HotkeyRegistration>(
                new AppError(ErrorCode.Registration, "combination occupied")));
        var failure = await fixture.Controller.ChangeHotkeyAsync(
            HotkeyBinding.Parse("Win", "T").Value);

        Assert.True(failure.IsFailure);
        Assert.Equal(proposed, fixture.Controller.State.Config.GetHotkeyBinding().Value);
    }

    [Fact]
    public async Task ChangeHotkeyCanRecoverAfterInitialRegistrationFailure()
    {
        var fixture = new ControllerFixture();
        fixture.Hotkeys.RegisterResults.Enqueue(
            Result.Failure<HotkeyRegistration>(new AppError(ErrorCode.Registration, "occupied")));
        await using var disposableFixture = fixture;
        await fixture.Controller.InitializeAsync();
        var proposed = HotkeyBinding.Parse("Alt", "F2").Value;

        var result = await fixture.Controller.ChangeHotkeyAsync(proposed);

        Assert.True(result.IsSuccess);
        Assert.True(fixture.Controller.State.HotkeyRegistered);
        Assert.Equal(proposed, fixture.Controller.State.Config.GetHotkeyBinding().Value);
    }

    [Fact]
    public async Task StartupChangeUsesActualResultAndHandlesUnavailableOrFailedService()
    {
        await using var fixture = await ControllerFixture.CreateAsync();

        var success = await fixture.Controller.SetStartupEnabledAsync(true);

        Assert.True(success.IsSuccess);
        Assert.True(fixture.Controller.State.StartupEnabled);

        fixture.Startup.SetResults.Enqueue(
            Result.Failure<StartupState>(new AppError(ErrorCode.Unavailable, "registry denied")));
        var failure = await fixture.Controller.SetStartupEnabledAsync(false);

        Assert.True(failure.IsFailure);
        Assert.True(fixture.Controller.State.StartupEnabled);

        var portable = new ControllerFixture();
        portable.Installation.State = new InstallationState(
            DeploymentMode.DevelopmentPortable,
            false,
            false);
        await using var portableDisposable = portable;
        await portable.Controller.InitializeAsync();

        var unavailable = await portable.Controller.SetStartupEnabledAsync(true);

        Assert.Equal(ErrorCode.Unavailable, unavailable.Error!.Code);
        Assert.Empty(portable.Startup.SetRequests);
    }

    [Fact]
    public async Task EnsureInstalledPublishesStateOrError()
    {
        await using var fixture = await ControllerFixture.CreateAsync();
        fixture.Installation.State = new InstallationState(
            DeploymentMode.Distribution,
            true,
            true,
            "Ready");

        var success = await fixture.Controller.EnsureInstalledAsync();

        Assert.True(success.IsSuccess);
        Assert.True(fixture.Controller.State.IsInstalled);
        Assert.Equal("Installed.", fixture.Controller.State.Notification);

        fixture.Installation.EnsureResultOverride = Result.Failure<InstallationResult>(
            new AppError(ErrorCode.Installation, "copy failed"));
        var failure = await fixture.Controller.EnsureInstalledAsync();

        Assert.Equal(ErrorCode.Installation, failure.Error!.Code);
        Assert.True(fixture.Controller.State.IsInstalled);
    }

    [Fact]
    public async Task UninstallRequestsExitOnlyAfterServiceHandshake()
    {
        await using var fixture = await ControllerFixture.CreateAsync();

        var success = await fixture.Controller.BeginUninstallAsync();

        Assert.True(success.IsSuccess);
        Assert.True(fixture.Controller.State.ExitRequested);
        Assert.False(fixture.Controller.State.IsInstalled);
        Assert.Equal("Cleanup started.", fixture.Controller.State.Notification);

        fixture.Installation.UninstallResultOverride = Result.Failure<InstallationResult>(
            new AppError(ErrorCode.Installation, "helper failed"));
        var failure = await fixture.Controller.BeginUninstallAsync();

        Assert.Equal(ErrorCode.Installation, failure.Error!.Code);
    }

    [Fact]
    public async Task PortableModeRejectsUninstallWithoutCallingService()
    {
        var fixture = new ControllerFixture();
        fixture.Installation.State = new InstallationState(
            DeploymentMode.DevelopmentPortable,
            false,
            false);
        await using var disposableFixture = fixture;
        await fixture.Controller.InitializeAsync();

        var result = await fixture.Controller.BeginUninstallAsync();

        Assert.Equal(ErrorCode.Unavailable, result.Error!.Code);
        Assert.False(fixture.Controller.State.ExitRequested);
    }

    [Fact]
    public async Task UnexpectedAdapterExceptionBecomesTypedFailureAndBusyStateClears()
    {
        await using var fixture = await ControllerFixture.CreateAsync();
        fixture.Displays.SnapshotHandler = _ => throw new InvalidOperationException("boom");

        var result = await fixture.Controller.RefreshDisplaysAsync();

        Assert.Equal(ErrorCode.Unknown, result.Error!.Code);
        Assert.False(fixture.Controller.State.IsBusy);
        Assert.IsType<InvalidOperationException>(result.Error.Exception);
    }

    [Fact]
    public async Task CancellationBecomesTypedFailureAndBusyStateClears()
    {
        await using var fixture = await ControllerFixture.CreateAsync();
        fixture.Displays.SnapshotHandler = async cancellationToken =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return Result.Success(TestSnapshots.Valid);
        };
        using var cancellation = new CancellationTokenSource();

        var pending = fixture.Controller.RefreshDisplaysAsync(cancellation.Token);
        cancellation.Cancel();
        var result = await pending;

        Assert.Equal(ErrorCode.Cancelled, result.Error!.Code);
        Assert.False(fixture.Controller.State.IsBusy);
    }

    [Fact]
    public async Task ViewSubscriberExceptionCannotBreakStateTransition()
    {
        await using var fixture = await ControllerFixture.CreateAsync();
        fixture.Controller.StateChanged += (_, _) => throw new InvalidOperationException("view failed");
        fixture.Displays.Snapshot = TestSnapshots.AtRate(120);

        var result = await fixture.Controller.RefreshDisplaysAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(120, fixture.Controller.State.Displays.PrimaryOrFirst!.CurrentRefreshRate);
    }

    [Fact]
    public async Task HotkeyEventQueuesSerializedToggle()
    {
        await using var fixture = await ControllerFixture.CreateAsync();

        fixture.Hotkeys.RaisePressed();
        await WaitUntilAsync(() => fixture.Displays.AppliedPlans.Count == 1);

        Assert.Single(fixture.Displays.AppliedPlans);
    }

    [Fact]
    public async Task DisplayEventUsesTrailingDebounce()
    {
        var scheduler = new ManualDelayScheduler();
        await using var fixture = await ControllerFixture.CreateAsync(scheduler);
        var callsBefore = fixture.Displays.SnapshotCalls;

        var first = fixture.Controller.QueueDisplayRefreshAsync();
        var second = fixture.Controller.QueueDisplayRefreshAsync();
        scheduler.ReleaseAll();
        await Task.WhenAll(first, second);

        Assert.Equal(callsBefore + 1, fixture.Displays.SnapshotCalls);
        Assert.All(scheduler.Requests, request =>
            Assert.Equal(RefreshToggleController.DisplayEventDebounce, request.Delay));
    }

    [Fact]
    public async Task ConcurrentControllerCommandsNeverOverlapAdapterCalls()
    {
        await using var fixture = await ControllerFixture.CreateAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var active = 0;
        var maximumActive = 0;
        fixture.Displays.SnapshotHandler = async cancellationToken =>
        {
            var current = Interlocked.Increment(ref active);
            maximumActive = Math.Max(maximumActive, current);
            if (Interlocked.Increment(ref calls) == 1)
            {
                entered.SetResult();
                await release.Task.WaitAsync(cancellationToken);
            }

            Interlocked.Decrement(ref active);
            return Result.Success(TestSnapshots.Valid);
        };

        var refresh = fixture.Controller.RefreshDisplaysAsync();
        await entered.Task;
        var toggle = fixture.Controller.ToggleAsync();

        Assert.Equal(1, calls);
        release.SetResult();
        await Task.WhenAll(refresh, toggle);

        Assert.Equal(1, maximumActive);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task DisposeUnregistersHotkeyAndDisposesNativePortOnce()
    {
        var fixture = await ControllerFixture.CreateAsync();

        await fixture.DisposeAsync();
        await fixture.DisposeAsync();

        Assert.True(fixture.Hotkeys.WasDisposed);
        Assert.Empty(fixture.Hotkeys.ActiveRegistrations);
        Assert.Throws<ObjectDisposedException>(() =>
        {
            _ = fixture.Controller.ToggleAsync();
        });
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!predicate())
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
        }
    }

    private sealed class ControllerFixture : IAsyncDisposable
    {
        internal ControllerFixture(IDelayScheduler? scheduler = null)
        {
            Controller = new RefreshToggleController(
                ConfigStore,
                Displays,
                Startup,
                Installation,
                Hotkeys,
                scheduler);
        }

        internal FakeConfigStore ConfigStore { get; } = new();

        internal FakeDisplayService Displays { get; } = new();

        internal FakeStartupService Startup { get; } = new();

        internal FakeInstallationService Installation { get; } = new();

        internal FakeHotkeyService Hotkeys { get; } = new();

        internal RefreshToggleController Controller { get; }

        internal static async Task<ControllerFixture> CreateAsync(IDelayScheduler? scheduler = null)
        {
            var fixture = new ControllerFixture(scheduler);
            var result = await fixture.Controller.InitializeAsync();
            Assert.True(result.IsSuccess);
            return fixture;
        }

        public ValueTask DisposeAsync() => Controller.DisposeAsync();
    }
}
