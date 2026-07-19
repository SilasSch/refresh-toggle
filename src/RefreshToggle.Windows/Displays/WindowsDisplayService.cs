using RefreshToggle.Application;
using RefreshToggle.Core;

namespace RefreshToggle.Windows.Displays;

internal sealed class WindowsDisplayService : IDisplayService
{
    private readonly IDisplayNativeBackend _backend;
    private readonly TimeProvider _timeProvider;

    public WindowsDisplayService(
        IDisplayNativeBackend? backend = null,
        TimeProvider? timeProvider = null)
    {
        _backend = backend ?? new User32DisplayNativeBackend();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task<Result<DisplaySnapshot>> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(Result.Failure<DisplaySnapshot>(AppError.Cancelled()));
        }

        return Task.FromResult(GetSnapshot());
    }

    public Task<Result<DisplaySnapshot>> ApplyAsync(
        RefreshRatePlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(Result.Failure<DisplaySnapshot>(AppError.Cancelled()));
        }

        var prepared = PrepareTransaction(plan);
        if (prepared.IsFailure)
        {
            return Task.FromResult(Result.Failure<DisplaySnapshot>(prepared.Error!));
        }

        // Nothing has changed yet. All displays must accept CDS_TEST before the
        // first real call is allowed.
        foreach (var change in prepared.Value)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromResult(Result.Failure<DisplaySnapshot>(AppError.Cancelled()));
            }

            var test = _backend.TestMode(change.DeviceName, change.TargetMode);
            if (!test.IsSuccess)
            {
                return Task.FromResult(Result.Failure<DisplaySnapshot>(new AppError(
                    ErrorCode.DisplayApply,
                    $"Windows rejected the {plan.TargetRate} Hz test for '{change.Label}': {test.Error}")));
            }
        }

        var applied = new List<PreparedDisplayChange>(prepared.Value.Count);
        foreach (var change in prepared.Value)
        {
            var apply = _backend.ApplyMode(change.DeviceName, change.TargetMode);
            if (!apply.IsSuccess)
            {
                var rollbackError = RollBack(applied);
                return Task.FromResult(Result.Failure<DisplaySnapshot>(AppError.Combine(
                    new AppError(
                        ErrorCode.DisplayApply,
                        $"Changing '{change.Label}' to {plan.TargetRate} Hz failed: {apply.Error}"),
                    rollbackError)));
            }

            applied.Add(change);

            var readBack = _backend.GetCurrentMode(change.DeviceName);
            if (!readBack.IsSuccess || readBack.Value!.RefreshRate != plan.TargetRate)
            {
                var actual = readBack.IsSuccess
                    ? $"Windows reported {readBack.Value!.RefreshRate} Hz"
                    : readBack.Error;
                var rollbackError = RollBack(applied);
                return Task.FromResult(Result.Failure<DisplaySnapshot>(AppError.Combine(
                    new AppError(
                        ErrorCode.DisplayApply,
                        $"The {plan.TargetRate} Hz change for '{change.Label}' could not be verified: {actual}."),
                    rollbackError)));
            }
        }

        var finalSnapshot = GetSnapshot();
        if (finalSnapshot.IsFailure)
        {
            return Task.FromResult(Result.Failure<DisplaySnapshot>(finalSnapshot.Error!));
        }

        var wrongRates = finalSnapshot.Value.Displays
            .Where(display => display.CurrentRefreshRate != plan.TargetRate)
            .Select(display => display.Label)
            .ToArray();
        if (wrongRates.Length > 0)
        {
            var rollbackError = RollBack(applied);
            return Task.FromResult(Result.Failure<DisplaySnapshot>(AppError.Combine(
                new AppError(
                    ErrorCode.DisplayApply,
                    $"The final refresh-rate verification failed for: {string.Join(", ", wrongRates)}."),
                rollbackError)));
        }

        return Task.FromResult(finalSnapshot);
    }

    private Result<DisplaySnapshot> GetSnapshot()
    {
        var devices = _backend.EnumerateActiveDisplays();
        if (!devices.IsSuccess)
        {
            return Result.Failure<DisplaySnapshot>(new AppError(
                ErrorCode.DisplayQuery,
                devices.Error ?? "Active displays could not be enumerated."));
        }

        var displays = new List<DisplayState>(devices.Value!.Count);
        foreach (var device in devices.Value)
        {
            var current = _backend.GetCurrentMode(device.DeviceName);
            if (!current.IsSuccess)
            {
                displays.Add(CreateFailedDisplayState(device, current.Error));
                continue;
            }

            var modes = _backend.EnumerateModes(device.DeviceName);
            if (!modes.IsSuccess)
            {
                displays.Add(new DisplayState(
                    device.DeviceName,
                    CreateLabel(device),
                    device.IsPrimary,
                    current.Value!.RefreshRate,
                    [],
                    new AppError(
                        ErrorCode.DisplayQuery,
                        modes.Error ?? $"Modes for '{device.DeviceName}' could not be enumerated.")));
                continue;
            }

            var currentMode = current.Value!;
            var supportedRates = modes.Value!
                .Where(mode => mode.Width == currentMode.Width &&
                               mode.Height == currentMode.Height &&
                               (currentMode.BitsPerPixel <= 0 ||
                                mode.BitsPerPixel <= 0 ||
                                mode.BitsPerPixel == currentMode.BitsPerPixel))
                .Select(mode => mode.RefreshRate)
                .Where(rate => rate > 1)
                .Distinct()
                .OrderBy(rate => rate)
                .ToArray();

            displays.Add(new DisplayState(
                device.DeviceName,
                CreateLabel(device),
                device.IsPrimary,
                currentMode.RefreshRate,
                supportedRates));
        }

        return Result.Success(new DisplaySnapshot(displays, _timeProvider.GetUtcNow()));
    }

    private Result<IReadOnlyList<PreparedDisplayChange>> PrepareTransaction(RefreshRatePlan plan)
    {
        if (plan.Changes.Count == 0)
        {
            return Result.Failure<IReadOnlyList<PreparedDisplayChange>>(
                AppError.Validation("A refresh-rate plan must contain at least one display."));
        }

        var devices = _backend.EnumerateActiveDisplays();
        if (!devices.IsSuccess)
        {
            return Result.Failure<IReadOnlyList<PreparedDisplayChange>>(new AppError(
                ErrorCode.DisplayQuery,
                devices.Error ?? "Active displays could not be enumerated."));
        }

        var planDeviceNames = plan.Changes
            .Select(change => change.DeviceName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var activeDeviceNames = devices.Value!
            .Select(device => device.DeviceName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!planDeviceNames.SetEquals(activeDeviceNames))
        {
            return Result.Failure<IReadOnlyList<PreparedDisplayChange>>(new AppError(
                ErrorCode.Conflict,
                "The active display set changed before the refresh-rate operation could start."));
        }

        var prepared = new List<PreparedDisplayChange>(plan.Changes.Count);
        foreach (var requestedChange in plan.Changes)
        {
            var device = devices.Value!.First(candidate =>
                string.Equals(candidate.DeviceName, requestedChange.DeviceName, StringComparison.OrdinalIgnoreCase));
            var current = _backend.GetCurrentMode(device.DeviceName);
            if (!current.IsSuccess)
            {
                return Result.Failure<IReadOnlyList<PreparedDisplayChange>>(new AppError(
                    ErrorCode.DisplayQuery,
                    current.Error ?? $"'{CreateLabel(device)}' is no longer available."));
            }

            if (current.Value!.RefreshRate != requestedChange.PreviousRate)
            {
                return Result.Failure<IReadOnlyList<PreparedDisplayChange>>(new AppError(
                    ErrorCode.Conflict,
                    $"'{CreateLabel(device)}' changed from {requestedChange.PreviousRate} Hz to " +
                    $"{current.Value.RefreshRate} Hz before the operation started."));
            }

            var modes = _backend.EnumerateModes(device.DeviceName);
            if (!modes.IsSuccess)
            {
                return Result.Failure<IReadOnlyList<PreparedDisplayChange>>(new AppError(
                    ErrorCode.DisplayQuery,
                    modes.Error ?? $"Modes for '{CreateLabel(device)}' could not be enumerated."));
            }

            var targetSupported = modes.Value!.Any(mode =>
                mode.RefreshRate == plan.TargetRate &&
                mode.Width == current.Value.Width &&
                mode.Height == current.Value.Height &&
                (current.Value.BitsPerPixel <= 0 ||
                 mode.BitsPerPixel <= 0 ||
                 mode.BitsPerPixel == current.Value.BitsPerPixel));
            if (!targetSupported)
            {
                return Result.Failure<IReadOnlyList<PreparedDisplayChange>>(new AppError(
                    ErrorCode.UnsupportedRefreshRate,
                    $"{plan.TargetRate} Hz is not supported by '{CreateLabel(device)}'."));
            }

            prepared.Add(new PreparedDisplayChange(
                device.DeviceName,
                CreateLabel(device),
                current.Value,
                current.Value with { RefreshRate = plan.TargetRate }));
        }

        return Result.Success<IReadOnlyList<PreparedDisplayChange>>(prepared);
    }

    private AppError? RollBack(IReadOnlyList<PreparedDisplayChange> applied)
    {
        var errors = new List<string>();
        for (var index = applied.Count - 1; index >= 0; index--)
        {
            var change = applied[index];
            var test = _backend.TestMode(change.DeviceName, change.PreviousMode);
            if (!test.IsSuccess)
            {
                errors.Add($"Rollback test failed for '{change.Label}': {test.Error}");
                continue;
            }

            var apply = _backend.ApplyMode(change.DeviceName, change.PreviousMode);
            if (!apply.IsSuccess)
            {
                errors.Add($"Rollback failed for '{change.Label}': {apply.Error}");
                continue;
            }

            var readBack = _backend.GetCurrentMode(change.DeviceName);
            if (!readBack.IsSuccess || readBack.Value!.RefreshRate != change.PreviousMode.RefreshRate)
            {
                errors.Add($"Rollback verification failed for '{change.Label}'.");
            }
        }

        return errors.Count == 0
            ? null
            : new AppError(ErrorCode.DisplayApply, string.Join(" ", errors));
    }

    private static DisplayState CreateFailedDisplayState(NativeDisplayDevice device, string? error) => new(
        device.DeviceName,
        CreateLabel(device),
        device.IsPrimary,
        currentRefreshRate: null,
        supportedRefreshRates: [],
        new AppError(
            ErrorCode.DisplayQuery,
            error ?? $"Current mode for '{device.DeviceName}' could not be queried."));

    private static string CreateLabel(NativeDisplayDevice device) =>
        device.IsPrimary ? $"{device.DisplayName} (Primary)" : device.DisplayName;

    private sealed record PreparedDisplayChange(
        string DeviceName,
        string Label,
        NativeDisplayMode PreviousMode,
        NativeDisplayMode TargetMode);
}
