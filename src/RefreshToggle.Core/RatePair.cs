namespace RefreshToggle.Core;

internal readonly record struct RatePair
{
    private RatePair(int rateA, int rateB)
    {
        RateA = rateA;
        RateB = rateB;
    }

    public int RateA { get; }

    public int RateB { get; }

    public static RatePair Default { get; } = new(60, 120);

    public static Result<RatePair> Create(int rateA, int rateB)
    {
        if (rateA <= 0 || rateB <= 0)
        {
            return Result.Failure<RatePair>(AppError.Validation("Refresh rates must be greater than zero."));
        }

        if (rateA == rateB)
        {
            return Result.Failure<RatePair>(AppError.Validation("Rate A and Rate B must be different."));
        }

        return Result.Success(new RatePair(rateA, rateB));
    }
}
