using System.Collections.Concurrent;
using RefreshToggle.Application;
using RefreshToggle.Core;

namespace RefreshToggle.Tests;

internal sealed class FakeConfigStore : IConfigStore
{
    internal Result<ConfigLoadResult> LoadResult { get; set; } =
        Result.Success(new ConfigLoadResult(AppConfig.Default, false, false));

    internal Queue<Result> SaveResults { get; } = new();

    internal List<AppConfig> SavedConfigs { get; } = [];

    internal Func<AppConfig, CancellationToken, Task<Result>>? SaveHandler { get; set; }

    internal int LoadCalls { get; private set; }

    public Task<Result<ConfigLoadResult>> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LoadCalls++;
        return Task.FromResult(LoadResult);
    }

    public Task<Result> SaveAsync(AppConfig config, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SavedConfigs.Add(config);
        if (SaveHandler is not null)
        {
            return SaveHandler(config, cancellationToken);
        }

        return Task.FromResult(SaveResults.Count == 0 ? Result.Success() : SaveResults.Dequeue());
    }
}

internal sealed class FakeDisplayService : IDisplayService
{
    internal DisplaySnapshot Snapshot { get; set; } = TestSnapshots.Valid;

    internal Func<CancellationToken, Task<Result<DisplaySnapshot>>>? SnapshotHandler { get; set; }

    internal Func<RefreshRatePlan, CancellationToken, Task<Result<DisplaySnapshot>>>? ApplyHandler { get; set; }

    internal List<RefreshRatePlan> AppliedPlans { get; } = [];

    internal int SnapshotCalls { get; private set; }

    public Task<Result<DisplaySnapshot>> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SnapshotCalls++;
        return SnapshotHandler is null
            ? Task.FromResult(Result.Success(Snapshot))
            : SnapshotHandler(cancellationToken);
    }

    public Task<Result<DisplaySnapshot>> ApplyAsync(
        RefreshRatePlan plan,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AppliedPlans.Add(plan);
        if (ApplyHandler is not null)
        {
            return ApplyHandler(plan, cancellationToken);
        }

        Snapshot = TestSnapshots.AtRate(plan.TargetRate);
        return Task.FromResult(Result.Success(Snapshot));
    }
}

internal sealed class FakeStartupService : IStartupService
{
    internal Result<StartupState> GetResult { get; set; } = Result.Success(new StartupState(false));

    internal Queue<Result<StartupState>> SetResults { get; } = new();

    internal List<bool> SetRequests { get; } = [];

    public Task<Result<StartupState>> GetStateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(GetResult);
    }

    public Task<Result<StartupState>> SetEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SetRequests.Add(enabled);
        return Task.FromResult(
            SetResults.Count == 0
                ? Result.Success(new StartupState(enabled))
                : SetResults.Dequeue());
    }
}

internal sealed class FakeInstallationService : IInstallationService
{
    internal InstallationState State { get; set; } = new(
        DeploymentMode.Distribution,
        IsInstalled: true,
        CanManageInstallation: true);

    internal Result<InstallationState>? GetResultOverride { get; set; }

    internal Result<InstallationResult>? EnsureResultOverride { get; set; }

    internal Result<InstallationResult>? UninstallResultOverride { get; set; }

    public Task<Result<InstallationState>> GetStateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(GetResultOverride ?? Result.Success(State));
    }

    public Task<Result<InstallationResult>> EnsureInstalledAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = new InstallationResult(State, true, false, "Installed.");
        return Task.FromResult(EnsureResultOverride ?? Result.Success(result));
    }

    public Task<Result<InstallationResult>> BeginUninstallAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var nextState = State with { IsInstalled = false };
        var result = new InstallationResult(nextState, false, true, "Cleanup started.");
        return Task.FromResult(UninstallResultOverride ?? Result.Success(result));
    }
}

internal sealed class FakeHotkeyService : IHotkeyService
{
    private int _nextSlot = 1;

    internal Queue<Result<HotkeyRegistration>> RegisterResults { get; } = new();

    internal Queue<Result> UnregisterResults { get; } = new();

    internal List<HotkeyRegistration> ActiveRegistrations { get; } = [];

    internal List<string> Operations { get; } = [];

    internal bool WasDisposed { get; private set; }

    public event EventHandler? HotkeyPressed;

    public Task<Result<HotkeyRegistration>> RegisterAsync(
        HotkeyBinding binding,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Operations.Add($"register:{binding.DisplayText}");

        Result<HotkeyRegistration> result;
        if (RegisterResults.Count > 0)
        {
            result = RegisterResults.Dequeue();
        }
        else
        {
            result = Result.Success(new HotkeyRegistration(_nextSlot, binding));
            _nextSlot = _nextSlot == 1 ? 2 : 1;
        }

        if (result.IsSuccess)
        {
            ActiveRegistrations.Add(result.Value);
        }

        return Task.FromResult(result);
    }

    public Task<Result> UnregisterAsync(
        HotkeyRegistration registration,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Operations.Add($"unregister:{registration.Binding.DisplayText}");
        var result = UnregisterResults.Count == 0 ? Result.Success() : UnregisterResults.Dequeue();
        if (result.IsSuccess)
        {
            ActiveRegistrations.Remove(registration);
        }

        return Task.FromResult(result);
    }

    internal void RaisePressed() => HotkeyPressed?.Invoke(this, EventArgs.Empty);

    public ValueTask DisposeAsync()
    {
        WasDisposed = true;
        ActiveRegistrations.Clear();
        return ValueTask.CompletedTask;
    }
}

internal sealed class ManualDelayScheduler : IDelayScheduler
{
    private readonly ConcurrentQueue<DelayRequest> _requests = new();

    internal IReadOnlyCollection<DelayRequest> Requests => _requests.ToArray();

    public ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var registration = cancellationToken.Register(
            static state => ((TaskCompletionSource)state!).TrySetCanceled(),
            completion);
        var request = new DelayRequest(delay, completion, registration);
        _requests.Enqueue(request);
        return new ValueTask(completion.Task);
    }

    internal void ReleaseAll()
    {
        while (_requests.TryDequeue(out var request))
        {
            request.Completion.TrySetResult();
            request.Registration.Dispose();
        }
    }

    internal sealed record DelayRequest(
        TimeSpan Delay,
        TaskCompletionSource Completion,
        CancellationTokenRegistration Registration);
}

internal static class TestSnapshots
{
    internal static DisplaySnapshot Valid { get; } = AtRate(60);

    internal static DisplaySnapshot AtRate(int rate) => new(
        [
            new DisplayState("DISPLAY1", "Primary", true, rate, [60, 120]),
            new DisplayState("DISPLAY2", "Secondary", false, rate, [60, 120])
        ],
        DateTimeOffset.UtcNow);
}
