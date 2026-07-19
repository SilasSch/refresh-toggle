using RefreshToggle.Core;

namespace RefreshToggle.Tests;

public sealed class RefreshRatePolicyTests
{
    private static readonly RatePair Rates = RatePair.Create(60, 120).Value;

    [Theory]
    [InlineData(60, 120)]
    [InlineData(120, 60)]
    [InlineData(61, 120)]
    [InlineData(119, 60)]
    [InlineData(90, 120)]
    [InlineData(int.MaxValue, 60)]
    [InlineData(int.MinValue, 120)]
    public void DetermineTargetPreservesLegacyNearestRateBehavior(int current, int expected)
    {
        Assert.Equal(expected, RefreshRatePolicy.DetermineTargetRate(current, Rates));
    }

    [Fact]
    public void RateOptionsUseUnionAndReportSupportCounts()
    {
        var snapshot = Snapshot(
            Display("DISPLAY1", true, 60, 60, 120, 144),
            Display("DISPLAY2", false, 60, 60, 120));

        var options = RefreshRatePolicy.GetSupportedRateOptions(snapshot);

        Assert.Collection(
            options,
            option => Assert.Equal(new RefreshRateSupport(60, 2, 2), option),
            option => Assert.Equal(new RefreshRateSupport(120, 2, 2), option),
            option => Assert.Equal(new RefreshRateSupport(144, 1, 2), option));
        Assert.True(options[0].IsSupportedByAll);
        Assert.False(options[2].IsSupportedByAll);
        Assert.Equal(new RefreshRateSupport(165, 0, 2), RefreshRatePolicy.GetSupport(snapshot, 165));
    }

    [Fact]
    public void TogglePlanUsesPrimaryRateAndIncludesEveryDisplay()
    {
        var snapshot = Snapshot(
            Display("SECONDARY", false, 120, 60, 120),
            Display("PRIMARY", true, 60, 60, 120));

        var result = RefreshRatePolicy.CreateTogglePlan(snapshot, Rates);

        Assert.True(result.IsSuccess);
        Assert.Equal(120, result.Value.TargetRate);
        Assert.Collection(
            result.Value.Changes,
            change => Assert.Equal(new DisplayRateChange("SECONDARY", 120, 120), change),
            change => Assert.Equal(new DisplayRateChange("PRIMARY", 60, 120), change));
    }

    [Fact]
    public void TogglePlanRejectsTargetUnsupportedByAnyDisplay()
    {
        var snapshot = Snapshot(
            Display("PRIMARY", true, 60, 60, 120),
            Display("SECONDARY", false, 60, 60));

        var result = RefreshRatePolicy.CreateTogglePlan(snapshot, Rates);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.UnsupportedRefreshRate, result.Error!.Code);
        Assert.Contains("SECONDARY", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TogglePlanRejectsEmptyOrUnavailableSnapshots()
    {
        var empty = RefreshRatePolicy.CreateTogglePlan(DisplaySnapshot.Empty, Rates);
        var unavailable = RefreshRatePolicy.CreateTogglePlan(
            Snapshot(new DisplayState(
                "DISPLAY1",
                "Broken display",
                true,
                null,
                [],
                new AppError(ErrorCode.DisplayQuery, "failed"))),
            Rates);

        Assert.Equal(ErrorCode.NotFound, empty.Error!.Code);
        Assert.Equal(ErrorCode.DisplayQuery, unavailable.Error!.Code);
    }

    [Fact]
    public void SnapshotFallsBackToFirstDisplayWhenNoPrimaryExists()
    {
        var snapshot = Snapshot(
            Display("FIRST", false, 60, 60, 120),
            Display("SECOND", false, 60, 60, 120));

        Assert.Equal("FIRST", snapshot.PrimaryOrFirst!.DeviceName);
        Assert.Null(DisplaySnapshot.Empty.PrimaryOrFirst);
    }

    private static DisplayState Display(
        string name,
        bool primary,
        int current,
        params int[] supported) =>
        new(name, name, primary, current, supported);

    private static DisplaySnapshot Snapshot(params DisplayState[] displays) =>
        new(displays, DateTimeOffset.UnixEpoch);
}
