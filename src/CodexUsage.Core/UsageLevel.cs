namespace CodexUsage.Core;

public enum UsageLevel
{
    Normal,
    Warning,
    Critical
}

public static class UsageLevelResolver
{
    public static UsageLevel FromPercentage(double percentage) => percentage switch
    {
        >= 90 => UsageLevel.Critical,
        >= 70 => UsageLevel.Warning,
        _ => UsageLevel.Normal
    };

    public static UsageLevel FromPercentage(
        double percentage,
        double warningThreshold,
        double criticalThreshold)
    {
        var normalized = Normalize(percentage);
        var critical = Math.Clamp(criticalThreshold, 1, 100);
        var warning = Math.Clamp(warningThreshold, 0, critical - 1);

        if (normalized >= critical)
        {
            return UsageLevel.Critical;
        }

        return normalized >= warning ? UsageLevel.Warning : UsageLevel.Normal;
    }

    public static double Normalize(double percentage) => Math.Clamp(percentage, 0, 100);

    public static double RemainingFromUsed(double usedPercentage) => 100 - Normalize(usedPercentage);
}
