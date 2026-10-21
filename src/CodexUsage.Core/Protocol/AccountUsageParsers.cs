using System.Globalization;
using System.Text;
using System.Text.Json;
using CodexUsage.Core.Models;
using CodexUsage.Core.Providers;

namespace CodexUsage.Core.Protocol;

/// <summary>
/// Parsers for the unofficial, opt-in account sources: the endpoints behind the Claude
/// and Cursor usage pages, authenticated with the sign-in each app saves locally.
/// These formats are undocumented, so every field is optional.
/// </summary>
public static class ClaudeAccountUsageParser
{
    /// <summary>Reads Claude Code's saved OAuth access token from <c>.credentials.json</c>.</summary>
    public static bool TryReadCredentials(string json, out string accessToken, out DateTimeOffset? expiresAt)
    {
        accessToken = string.Empty;
        expiresAt = null;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var oauth = root.TryGetProperty("claudeAiOauth", out var nested) && nested.ValueKind == JsonValueKind.Object
                ? nested
                : root;
            if (!oauth.TryGetProperty("accessToken", out var token) ||
                token.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(token.GetString()))
            {
                return false;
            }

            accessToken = token.GetString()!;
            if (oauth.TryGetProperty("expiresAt", out var expiry) &&
                ClaudeStatusLineParser.TryReadTimestamp(expiry, out var parsed))
            {
                expiresAt = parsed;
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Parses <c>GET /api/oauth/usage</c>: <c>five_hour</c> and <c>seven_day</c> utilization.</summary>
    public static bool TryParseUsage(string json, out ClaudeRateLimits limits)
    {
        limits = new ClaudeRateLimits(null, null);
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            limits = new ClaudeRateLimits(
                ReadWindow(root, "five_hour", ClaudeStatusLineParser.FiveHourMinutes),
                ReadWindow(root, "seven_day", ClaudeStatusLineParser.SevenDayMinutes));
            return limits.HasAny;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static QuotaWindow? ReadWindow(JsonElement root, string name, int minutes)
    {
        if (!root.TryGetProperty(name, out var window) ||
            window.ValueKind != JsonValueKind.Object ||
            !window.TryGetProperty("utilization", out var utilization) ||
            !utilization.TryGetDouble(out var used) ||
            double.IsNaN(used) || double.IsInfinity(used))
        {
            return null;
        }

        DateTimeOffset? resetsAt = window.TryGetProperty("resets_at", out var reset) &&
                                   ClaudeStatusLineParser.TryReadTimestamp(reset, out var parsed)
            ? parsed
            : null;
        return new QuotaWindow(UsageLevelResolver.Normalize(used), minutes, resetsAt);
    }
}

public static class CursorAccountUsageParser
{
    /// <summary>
    /// Parses Cursor's dashboard usage (<c>cursor.com/api/usage-summary</c>) or the editor's
    /// <c>GetCurrentPeriodUsage</c> response into a state for the rail.
    /// </summary>
    public static bool TryParse(string json, DateTimeOffset now, out ProviderUsageState state)
    {
        state = ProviderUsageState.Off(UsageProviderKind.Cursor);
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            // usage-summary nests the plan under individualUsage; the editor API uses planUsage.
            JsonElement plan = default;
            var hasPlan =
                (root.TryGetProperty("individualUsage", out var individual) &&
                 individual.ValueKind == JsonValueKind.Object &&
                 individual.TryGetProperty("plan", out plan) &&
                 plan.ValueKind == JsonValueKind.Object) ||
                (root.TryGetProperty("planUsage", out plan) && plan.ValueKind == JsonValueKind.Object);
            if (!hasPlan)
            {
                return false;
            }

            double? percent = ReadNumber(plan, "totalPercentUsed");
            if (percent is null &&
                ReadNumber(plan, "used") is { } used &&
                ReadNumber(plan, "limit") is { } limit && limit > 0)
            {
                percent = used / limit * 100;
            }

            var resetsAt = ReadTime(root, "billingCycleEnd");
            if (resetsAt is null && ReadTime(root, "billingCycleStart") is { } start)
            {
                resetsAt = CursorSpendParser.NextCycle(start, now);
            }

            var details = new List<string>();
            if (ReadNumber(plan, "autoPercentUsed") is { } auto)
            {
                details.Add($"Auto {Math.Round(auto)}%");
            }

            if (ReadNumber(plan, "apiPercentUsed") is { } api)
            {
                details.Add($"API {Math.Round(api)}%");
            }

            var summary = details.Count > 0 ? string.Join("  ·  ", details) : null;
            if (root.TryGetProperty("isUnlimited", out var unlimited) && unlimited.ValueKind == JsonValueKind.True)
            {
                summary = "Unlimited plan";
            }

            var windows = percent is { } value
                ? new[]
                {
                    new UsageWindowDisplay(
                        "cursor:plan",
                        "Included usage",
                        new QuotaWindow(UsageLevelResolver.Normalize(value), null, resetsAt))
                }
                : Array.Empty<UsageWindowDisplay>();
            state = new ProviderUsageState(
                UsageProviderKind.Cursor,
                ProviderConnection.Live,
                Formatting.RelativeTimeFormatter.FormatAge(now, now),
                windows.Length == 0 ? "Cursor didn’t report a usage percentage for this plan." : string.Empty,
                windows,
                now,
                summary);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
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

    private static DateTimeOffset? ReadTime(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && ClaudeStatusLineParser.TryReadTimestamp(value, out var parsed)
            ? parsed
            : null;
}

public static class JwtClaims
{
    /// <summary>Reads the unverified <c>sub</c> and <c>exp</c> claims; the server still validates the token.</summary>
    public static bool TryRead(string token, out string subject, out DateTimeOffset? expiresAt)
    {
        subject = string.Empty;
        expiresAt = null;
        var parts = token.Split('.');
        if (parts.Length != 3 || parts[1].Length is 0 or > 16_384)
        {
            return false;
        }

        try
        {
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            using var document = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
            var root = document.RootElement;
            if (root.TryGetProperty("sub", out var sub) && sub.ValueKind == JsonValueKind.String)
            {
                subject = sub.GetString() ?? string.Empty;
            }

            if (root.TryGetProperty("exp", out var exp) && ClaudeStatusLineParser.TryReadTimestamp(exp, out var parsed))
            {
                expiresAt = parsed;
            }

            return subject.Length > 0;
        }
        catch (Exception exception) when (exception is FormatException or JsonException or ArgumentException)
        {
            return false;
        }
    }
}
