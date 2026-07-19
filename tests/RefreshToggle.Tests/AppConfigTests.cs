using RefreshToggle.Core;

namespace RefreshToggle.Tests;

public sealed class AppConfigTests
{
    [Fact]
    public void DefaultSchemaAndValuesAreValid()
    {
        Assert.Equal(AppConfig.CurrentSchemaVersion, AppConfig.Default.SchemaVersion);
        Assert.True(AppConfig.Default.Validate().IsSuccess);
        Assert.Equal(RatePair.Default, AppConfig.Default.GetRatePair().Value);
        Assert.Equal(HotkeyBinding.Default, AppConfig.Default.GetHotkeyBinding().Value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ValidateRejectsUnsupportedSchema(int schema)
    {
        var config = AppConfig.Default with { SchemaVersion = schema };

        Assert.Equal(ErrorCode.Validation, config.Validate().Error!.Code);
    }

    [Fact]
    public void ValidateRejectsInvalidRatesAndHotkey()
    {
        var rates = AppConfig.Default with { RateB = AppConfig.Default.RateA };
        var hotkey = AppConfig.Default with { HotkeyKey = "82" };

        Assert.True(rates.Validate().IsFailure);
        Assert.True(hotkey.Validate().IsFailure);
    }

    [Fact]
    public void WithMethodsReturnNewNormalizedConfig()
    {
        var rates = RatePair.Create(75, 165).Value;
        var binding = HotkeyBinding.Parse("Alt+Win", "F12").Value;

        var changed = AppConfig.Default.WithRates(rates).WithHotkey(binding);

        Assert.Equal(75, changed.RateA);
        Assert.Equal(165, changed.RateB);
        Assert.Equal("Alt+Win", changed.HotkeyModifiers);
        Assert.Equal("F12", changed.HotkeyKey);
        Assert.NotEqual(changed, AppConfig.Default);
    }
}
