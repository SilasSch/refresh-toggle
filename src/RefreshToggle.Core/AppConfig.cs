namespace RefreshToggle.Core;

internal sealed record AppConfig
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public int RateA { get; init; } = RatePair.Default.RateA;

    public int RateB { get; init; } = RatePair.Default.RateB;

    public string HotkeyModifiers { get; init; } = HotkeyBinding.Default.ModifierText;

    public string HotkeyKey { get; init; } = HotkeyBinding.Default.KeyText;

    public static AppConfig Default { get; } = new();

    public Result<RatePair> GetRatePair() => RatePair.Create(RateA, RateB);

    public Result<HotkeyBinding> GetHotkeyBinding() =>
        HotkeyBinding.Parse(HotkeyModifiers, HotkeyKey);

    public Result Validate()
    {
        if (SchemaVersion is <= 0 or > CurrentSchemaVersion)
        {
            return Result.Failure(
                AppError.Validation($"Unsupported config schema version: {SchemaVersion}."));
        }

        var rates = GetRatePair();
        if (rates.IsFailure)
        {
            return Result.Failure(rates.Error!);
        }

        var hotkey = GetHotkeyBinding();
        return hotkey.IsFailure
            ? Result.Failure(hotkey.Error!)
            : Result.Success();
    }

    public AppConfig WithRates(RatePair rates) => this with
    {
        RateA = rates.RateA,
        RateB = rates.RateB
    };

    public AppConfig WithHotkey(HotkeyBinding binding) => this with
    {
        HotkeyModifiers = binding.ModifierText,
        HotkeyKey = binding.KeyText
    };
}
