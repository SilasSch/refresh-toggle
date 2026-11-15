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
