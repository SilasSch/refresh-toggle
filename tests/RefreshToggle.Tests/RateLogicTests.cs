namespace RefreshToggle.Tests;

public class RateLogicTests
{
    [Theory]
    [InlineData(60, 120)]
    [InlineData(120, 60)]
    [InlineData(59, 120)]  // closer to A → go to B
    [InlineData(144, 60)]  // closer to B → go to A
    [InlineData(90, 120)]  // equally close → go to B
    public void NextRate_switches_away_from_the_closer_profile(int current, int expected) =>
        Assert.Equal(expected, RateLogic.NextRate(current, rateA: 60, rateB: 120));

    [Fact]
    public void CommonRates_returns_sorted_intersection()
    {
        int[][] monitors = [[144, 60, 120], [60, 75, 120]];
        Assert.Equal([60, 120], RateLogic.CommonRates(monitors));
    }

    [Fact]
    public void CommonRates_of_no_monitors_is_empty() =>
        Assert.Empty(RateLogic.CommonRates([]));
}

public class PowerSourceTests
{
    [Theory]
    [InlineData(true, 60, 120, 120)]
    [InlineData(true, 120, 60, 120)]
    [InlineData(false, 60, 120, 60)]
    [InlineData(false, 144, 60, 60)]
    public void RateForPowerSource_picks_higher_when_plugged_in(bool pluggedIn, int rateA, int rateB, int expected) =>
        Assert.Equal(expected, RateLogic.RateForPowerSource(pluggedIn, rateA, rateB));

    [Fact]
    public void First_known_state_is_reported() =>
        Assert.True(new PowerSourceTracker().Observe(true));

    [Fact]
    public void Same_state_again_is_not_a_change()
    {
        var tracker = new PowerSourceTracker();
        tracker.Observe(true);
        Assert.Null(tracker.Observe(true)); // e.g. a battery-percentage tick
    }

    [Fact]
    public void Real_change_is_reported()
    {
        var tracker = new PowerSourceTracker();
        tracker.Observe(true);
        Assert.False(tracker.Observe(false));
    }

    [Fact]
    public void Unknown_is_ignored_and_does_not_reset()
    {
        var tracker = new PowerSourceTracker();
        tracker.Observe(true);
        Assert.Null(tracker.Observe(null));
        Assert.Null(tracker.Observe(true));
        Assert.False(tracker.Observe(false));
    }
}
