namespace CodexUsage.Core.Formatting;

public static class RelativeTimeFormatter
{
    public static string FormatAge(DateTimeOffset updatedAt, DateTimeOffset now)
    {
        var age = now - updatedAt;
        if (age < TimeSpan.FromMinutes(1))
        {
            return "Updated just now";
        }

        if (age < TimeSpan.FromHours(1))
        {
            return $"Updated {(int)age.TotalMinutes}m ago";
        }

        if (age < TimeSpan.FromDays(1))
        {
            return $"Updated {(int)age.TotalHours}h ago";
        }

        return $"Updated {(int)age.TotalDays}d ago";
    }
}
