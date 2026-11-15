namespace RefreshToggle;

/// <summary>Pure refresh-rate decisions, kept free of Win32 so they can be unit-tested anywhere.</summary>
internal static class RateLogic
{
    /// <summary>
    /// The rate to switch to: the other profile when the display is on A or B, otherwise away from
    /// whichever profile is closer.
    /// </summary>
    public static int NextRate(int currentRate, int rateA, int rateB)
    {
        if (currentRate == rateA)
        {
            return rateB;
        }

        if (currentRate == rateB)
        {
            return rateA;
        }

        return Math.Abs(currentRate - rateA) <= Math.Abs(currentRate - rateB) ? rateB : rateA;
    }

    /// <summary>Rates that every monitor supports, ascending.</summary>
    public static IReadOnlyList<int> CommonRates(IEnumerable<IEnumerable<int>> ratesPerMonitor)
    {
        HashSet<int>? common = null;
        foreach (var rates in ratesPerMonitor)
        {
            if (common is null)
            {
                common = [.. rates];
            }
            else
            {
                common.IntersectWith(rates);
            }
        }

        return common is null ? [] : [.. common.Order()];
    }
}
