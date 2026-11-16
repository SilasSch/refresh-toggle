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

    /// <summary>The higher of the two rates on mains power, the lower one on battery.</summary>
    public static int RateForPowerSource(bool pluggedIn, int rateA, int rateB) =>
        pluggedIn ? Math.Max(rateA, rateB) : Math.Min(rateA, rateB);

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

/// <summary>
/// Turns Windows' noisy power notifications (one per battery-percentage tick) into real
/// plugged-in / on-battery transitions, so a manual toggle is only overridden by an actual change.
/// </summary>
internal sealed class PowerSourceTracker
{
    private bool? _last;

    /// <summary>
    /// Feed the current state (null = unknown). Returns the state when it is the first known one or
    /// differs from the last known one; otherwise null.
    /// </summary>
    public bool? Observe(bool? pluggedIn)
    {
        if (pluggedIn is null || pluggedIn == _last)
        {
            return null;
        }

        _last = pluggedIn;
        return pluggedIn;
    }
}
