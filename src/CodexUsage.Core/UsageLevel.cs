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

    public static double Normalize(double percentage) => Math.Clamp(percentage, 0, 100);
}

