namespace CodexUsage.Core.Formatting;

public static class ResetTimeFormatter
{
    public static string Format(DateTimeOffset? resetsAt, DateTimeOffset now)
    {
        if (resetsAt is null)
        {
            return "Reset time isn’t available";
        }

        var remaining = resetsAt.Value - now;
        if (remaining <= TimeSpan.Zero)
        {
            return "Resets now";
        }

        if (remaining.TotalDays >= 1)
        {
            return $"Resets in {(int)remaining.TotalDays}d {remaining.Hours}h";
        }

        if (remaining.TotalHours >= 1)
        {
            return $"Resets in {(int)remaining.TotalHours}h {remaining.Minutes}m";
        }

        return $"Resets in {Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes))}m";
    }
}
