namespace RestMode.Core.Safety;

/// <summary>
/// Overheat protection, evaluated every 30 s while resting:
///  1. Hottest of CPU/GPU at or above the limit -> put the fans back to Normal.
///  2. Still at/above the limit for 3 more checks (90 s), or 5 °C over it
///     -> leave Rest Mode by sleeping the PC (sleep stops the heat;
///        waking up normally would only make it hotter).
/// The limit is stricter (5 °C lower) while the lid is closed (less airflow).
/// RestMode never sets a fan to 0 % - it only switches between the
/// firmware's / Fan Control's own profiles, or back to Normal.
/// </summary>
internal sealed class SafetyMonitor
{
    public enum Step { None, FansToNormal, Sleep }

    private int _overCount;

    public bool FansReturnedToNormal { get; private set; }
    public double? Peak { get; private set; }

    public void Reset()
    {
        _overCount = 0;
        FansReturnedToNormal = false;
        Peak = null;
    }

    public static int LimitFor(AppSettings s, DeviceProfile dev, bool lidClosed)
    {
        int limit = s.TempLimitC > 0 ? s.TempLimitC : dev.DefaultTempLimitC;
        return lidClosed ? limit - 5 : limit;
    }

    public Step Evaluate(double? hottest, int limit)
    {
        if (hottest is not double t) return Step.None;
        if (Peak is not double p || t > p) Peak = t;

        if (t < limit)
        {
            if (t < limit - 5) _overCount = 0;
            return Step.None;
        }
        if (!FansReturnedToNormal)
        {
            FansReturnedToNormal = true;
            return Step.FansToNormal;
        }
        _overCount++;
        return t >= limit + 5 || _overCount >= 3 ? Step.Sleep : Step.None;
    }
}
