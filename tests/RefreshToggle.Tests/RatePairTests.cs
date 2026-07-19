using RefreshToggle.Core;

namespace RefreshToggle.Tests;

public sealed class RatePairTests
{
    [Fact]
    public void DefaultUsesLegacyRates()
    {
        Assert.Equal(60, RatePair.Default.RateA);
        Assert.Equal(120, RatePair.Default.RateB);
    }

    [Theory]
    [InlineData(0, 120)]
    [InlineData(-1, 120)]
    [InlineData(60, 0)]
    [InlineData(60, -1)]
    [InlineData(60, 60)]
    public void CreateRejectsInvalidPairs(int rateA, int rateB)
    {
        var result = RatePair.Create(rateA, rateB);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.Validation, result.Error!.Code);
    }

    [Fact]
    public void CreateReturnsRequestedRates()
    {
        var result = RatePair.Create(75, 165);

        Assert.True(result.IsSuccess);
        Assert.Equal(75, result.Value.RateA);
        Assert.Equal(165, result.Value.RateB);
    }
}
