namespace CodexUsage.Core.Models;

public sealed record UsageWindowDisplay(
    string Key,
    string Label,
    QuotaWindow Window);

public static class UsageWindowDisplayBuilder
{
    private const int FiveHourWindowMinutes = 300;
    private const int WeeklyWindowMinutes = 10_080;

    public static IReadOnlyList<UsageWindowDisplay> Build(UsageSnapshot snapshot)
    {
        var rows = new List<UsageWindowDisplay>();
        AddBucket(rows, snapshot.Primary, isPrimaryBucket: true);
        return rows;
    }

    private static void AddBucket(
        List<UsageWindowDisplay> rows,
        RateLimitBucket bucket,
        bool isPrimaryBucket)
    {
        rows.Add(new UsageWindowDisplay(
            $"{bucket.Id}:primary",
            ResolveLabel(bucket, bucket.Primary, isPrimaryBucket, isSecondary: false),
            bucket.Primary));

        if (bucket.Secondary is { } secondary)
        {
            rows.Add(new UsageWindowDisplay(
                $"{bucket.Id}:secondary",
                ResolveLabel(bucket, secondary, isPrimaryBucket, isSecondary: true),
                secondary));
        }
    }

    private static string ResolveLabel(
        RateLimitBucket bucket,
        QuotaWindow window,
        bool isPrimaryBucket,
        bool isSecondary)
    {
        var windowName = window.WindowDurationMinutes switch
        {
            FiveHourWindowMinutes => "5-hour limit",
            WeeklyWindowMinutes => "Weekly limit",
            _ => null
        };

        if (isPrimaryBucket && windowName is not null)
        {
            return windowName;
        }

        if (isSecondary)
        {
            return $"{bucket.DisplayName} · secondary limit";
        }

        return bucket.DisplayName;
    }
}
