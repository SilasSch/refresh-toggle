using System.Collections.ObjectModel;

namespace RefreshToggle.Core;

internal sealed record DisplayState
{
    public DisplayState(
        string deviceName,
        string label,
        bool isPrimary,
        int? currentRefreshRate,
        IEnumerable<int> supportedRefreshRates,
        AppError? queryError = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(supportedRefreshRates);

        DeviceName = deviceName;
        Label = label;
        IsPrimary = isPrimary;
        CurrentRefreshRate = currentRefreshRate;
        SupportedRefreshRates = new ReadOnlyCollection<int>(
            supportedRefreshRates
                .Where(rate => rate > 1)
                .Distinct()
                .OrderBy(rate => rate)
                .ToArray());
        QueryError = queryError;
    }

    public string DeviceName { get; }

    public string Label { get; }

    public bool IsPrimary { get; }

    public int? CurrentRefreshRate { get; }

    public IReadOnlyList<int> SupportedRefreshRates { get; }

    public AppError? QueryError { get; }

    public bool IsAvailable => QueryError is null && CurrentRefreshRate is > 1;

    public bool Supports(int refreshRate) => SupportedRefreshRates.Contains(refreshRate);
}

internal sealed record DisplaySnapshot
{
    public DisplaySnapshot(IEnumerable<DisplayState> displays, DateTimeOffset capturedAt)
    {
        ArgumentNullException.ThrowIfNull(displays);
        Displays = new ReadOnlyCollection<DisplayState>(displays.ToArray());
        CapturedAt = capturedAt;
    }

    public IReadOnlyList<DisplayState> Displays { get; }

    public DateTimeOffset CapturedAt { get; }

    public DisplayState? PrimaryOrFirst
    {
        get
        {
            var primary = Displays.FirstOrDefault(display => display.IsPrimary);
            return primary ?? (Displays.Count == 0 ? null : Displays[0]);
        }
    }

    public static DisplaySnapshot Empty { get; } = new([], DateTimeOffset.MinValue);
}

internal sealed record RefreshRateSupport(
    int Rate,
    int SupportedDisplayCount,
    int TotalDisplayCount)
{
    public bool IsSupportedByAll => TotalDisplayCount > 0 && SupportedDisplayCount == TotalDisplayCount;
}

internal sealed record DisplayRateChange(string DeviceName, int PreviousRate, int TargetRate);

internal sealed record RefreshRatePlan
{
    public RefreshRatePlan(int targetRate, IEnumerable<DisplayRateChange> changes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetRate);

        ArgumentNullException.ThrowIfNull(changes);
        TargetRate = targetRate;
        Changes = new ReadOnlyCollection<DisplayRateChange>(changes.ToArray());
    }

    public int TargetRate { get; }

    public IReadOnlyList<DisplayRateChange> Changes { get; }
}
