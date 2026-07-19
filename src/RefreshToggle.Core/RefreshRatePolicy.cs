namespace RefreshToggle.Core;

internal static class RefreshRatePolicy
{
    public static int DetermineTargetRate(int currentRate, RatePair rates)
    {
        if (currentRate == rates.RateA)
        {
            return rates.RateB;
        }

        if (currentRate == rates.RateB)
        {
            return rates.RateA;
        }

        var distanceToA = Math.Abs((long)currentRate - rates.RateA);
        var distanceToB = Math.Abs((long)currentRate - rates.RateB);
        return distanceToA <= distanceToB ? rates.RateB : rates.RateA;
    }

    public static IReadOnlyList<RefreshRateSupport> GetSupportedRateOptions(DisplaySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return snapshot.Displays
            .SelectMany(display => display.SupportedRefreshRates)
            .Distinct()
            .OrderBy(rate => rate)
            .Select(rate => new RefreshRateSupport(
                rate,
                snapshot.Displays.Count(display => display.Supports(rate)),
                snapshot.Displays.Count))
            .ToArray();
    }

    public static RefreshRateSupport GetSupport(DisplaySnapshot snapshot, int rate)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new RefreshRateSupport(
            rate,
            snapshot.Displays.Count(display => display.Supports(rate)),
            snapshot.Displays.Count);
    }

    public static Result<RefreshRatePlan> CreateTogglePlan(DisplaySnapshot snapshot, RatePair rates)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (snapshot.Displays.Count == 0)
        {
            return Result.Failure<RefreshRatePlan>(
                new AppError(ErrorCode.NotFound, "No active displays were detected."));
        }

        var unavailableDisplays = snapshot.Displays
            .Where(display => !display.IsAvailable)
            .Select(display => display.Label)
            .ToArray();
        if (unavailableDisplays.Length > 0)
        {
            return Result.Failure<RefreshRatePlan>(
                new AppError(
                    ErrorCode.DisplayQuery,
                    $"Refresh-rate information is unavailable for: {string.Join(", ", unavailableDisplays)}."));
        }

        var referenceDisplay = snapshot.PrimaryOrFirst!;
        var targetRate = DetermineTargetRate(referenceDisplay.CurrentRefreshRate!.Value, rates);
        var unsupportedDisplays = snapshot.Displays
            .Where(display => !display.Supports(targetRate))
            .Select(display => display.Label)
            .ToArray();
        if (unsupportedDisplays.Length > 0)
        {
            return Result.Failure<RefreshRatePlan>(
                new AppError(
                    ErrorCode.UnsupportedRefreshRate,
                    $"{targetRate} Hz is not supported by: {string.Join(", ", unsupportedDisplays)}."));
        }

        var changes = snapshot.Displays.Select(display => new DisplayRateChange(
            display.DeviceName,
            display.CurrentRefreshRate!.Value,
            targetRate));
        return Result.Success(new RefreshRatePlan(targetRate, changes));
    }
}
