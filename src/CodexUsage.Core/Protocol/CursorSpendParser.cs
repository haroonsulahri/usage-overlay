using System.Globalization;
using System.Text.Json;
using CodexUsage.Core.Formatting;
using CodexUsage.Core.Models;
using CodexUsage.Core.Providers;

namespace CodexUsage.Core.Protocol;

public sealed record CursorMemberSpend(
    double SpendCents,
    double? OverallSpendCents,
    double? LimitDollars,
    double? PercentUsed,
    DateTimeOffset? CycleStart);

/// <summary>
/// Parses the Cursor Admin API <c>POST /teams/spend</c> response for one member. The live
/// schema has more fields than the public docs, so every field except the email match
/// and <c>spendCents</c> is optional.
/// </summary>
public static class CursorSpendParser
{
    public static bool TryParse(string json, string email, out CursorMemberSpend spend)
    {
        spend = new CursorMemberSpend(0, null, null, null, null);
        if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(email) || json.Length > 4_194_304)
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("teamMemberSpend", out var members) ||
                members.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            DateTimeOffset? cycleStart = null;
            if (root.TryGetProperty("subscriptionCycleStart", out var cycle) &&
                ClaudeStatusLineParser.TryReadTimestamp(cycle, out var parsedCycle))
            {
                cycleStart = parsedCycle;
            }

            foreach (var member in members.EnumerateArray())
            {
                if (member.ValueKind != JsonValueKind.Object ||
                    !member.TryGetProperty("email", out var memberEmail) ||
                    memberEmail.ValueKind != JsonValueKind.String ||
                    !string.Equals(memberEmail.GetString()?.Trim(), email.Trim(), StringComparison.OrdinalIgnoreCase) ||
                    ReadNumber(member, "spendCents") is not { } spendCents)
                {
                    continue;
                }

                var limit = PositiveOrNull(ReadNumber(member, "hardLimitOverrideDollars")) ??
                            PositiveOrNull(ReadNumber(member, "monthlyLimitDollars"));
                spend = new CursorMemberSpend(
                    Math.Max(0, spendCents),
                    ReadNumber(member, "overallSpendCents"),
                    limit,
                    ReadNumber(member, "totalPercentUsed"),
                    cycleStart);
                return true;
            }

            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static ProviderUsageState ToState(CursorMemberSpend spend, DateTimeOffset now)
    {
        var summary = FormatSummary(spend);
        double? usedPercent = spend.PercentUsed is { } percent
            ? percent
            : spend.LimitDollars is { } limit
                ? spend.SpendCents / (limit * 100) * 100
                : null;
        var windows = usedPercent is { } used
            ? new[]
            {
                new UsageWindowDisplay(
                    "cursor:cycle",
                    spend.LimitDollars is null ? "Included usage" : "Monthly spend limit",
                    new QuotaWindow(UsageLevelResolver.Normalize(used), null, NextCycle(spend.CycleStart, now)))
            }
            : Array.Empty<UsageWindowDisplay>();

        return new ProviderUsageState(
            UsageProviderKind.Cursor,
            ProviderConnection.Live,
            RelativeTimeFormatter.FormatAge(now, now),
            windows.Length == 0 ? "No spend limit is set, so there is no percentage to show." : string.Empty,
            windows,
            now,
            summary);
    }

    public static DateTimeOffset? NextCycle(DateTimeOffset? cycleStart, DateTimeOffset now)
    {
        if (cycleStart is not { } start || start > now.AddYears(1) || start < now.AddYears(-5))
        {
            return null;
        }

        var next = start;
        while (next <= now)
        {
            next = next.AddMonths(1);
        }

        return next;
    }

    private static string FormatSummary(CursorMemberSpend spend)
    {
        var culture = CultureInfo.GetCultureInfo("en-US");
        var onDemand = (spend.SpendCents / 100).ToString("C2", culture);
        var text = spend.LimitDollars is { } limit
            ? $"{onDemand} of {limit.ToString("C0", culture)} on-demand"
            : $"{onDemand} on-demand";
        return spend.OverallSpendCents is { } overall && overall > 0
            ? $"{text}  ·  {(overall / 100).ToString("C2", culture)} total"
            : text;
    }

    private static double? ReadNumber(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) &&
            !double.IsNaN(number) && !double.IsInfinity(number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String &&
               double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static double? PositiveOrNull(double? value) => value is > 0 ? value : null;
}
