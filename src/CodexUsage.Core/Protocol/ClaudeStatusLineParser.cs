using System.Globalization;
using System.Text.Json;
using CodexUsage.Core.Models;

namespace CodexUsage.Core.Protocol;

public sealed record ClaudeRateLimits(QuotaWindow? FiveHour, QuotaWindow? SevenDay)
{
    public bool HasAny => FiveHour is not null || SevenDay is not null;
}

/// <summary>
/// Reads the plan limits Claude Code passes to its status-line command on stdin. The
/// <c>rate_limits</c> object is only present for Claude.ai Pro and Max sign-ins, and only
/// after the first response of a session.
/// </summary>
public static class ClaudeStatusLineParser
{
    public const int MaximumInputLength = 1_048_576;
    public const int FiveHourMinutes = 300;
    public const int SevenDayMinutes = 10_080;

    public static bool TryParse(
        string json,
        out ClaudeRateLimits limits,
        out string? sessionId)
    {
        limits = new ClaudeRateLimits(null, null);
        sessionId = null;
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaximumInputLength)
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (root.TryGetProperty("session_id", out var session) && session.ValueKind == JsonValueKind.String)
            {
                var value = session.GetString() ?? string.Empty;
                sessionId = value.Length is > 0 and <= 128 ? value : null;
            }

            if (root.TryGetProperty("rate_limits", out var rateLimits) &&
                rateLimits.ValueKind == JsonValueKind.Object)
            {
                limits = new ClaudeRateLimits(
                    ParseWindow(rateLimits, "five_hour", FiveHourMinutes),
                    ParseWindow(rateLimits, "seven_day", SevenDayMinutes));
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static QuotaWindow? ParseWindow(JsonElement rateLimits, string name, int durationMinutes)
    {
        if (!rateLimits.TryGetProperty(name, out var window) ||
            window.ValueKind != JsonValueKind.Object ||
            !window.TryGetProperty("used_percentage", out var used) ||
            !used.TryGetDouble(out var usedPercent) ||
            double.IsNaN(usedPercent) ||
            double.IsInfinity(usedPercent))
        {
            return null;
        }

        DateTimeOffset? resetsAt = null;
        if ((window.TryGetProperty("resets_at", out var reset) || window.TryGetProperty("reset_at", out reset)) &&
            TryReadTimestamp(reset, out var parsed))
        {
            resetsAt = parsed;
        }

        return new QuotaWindow(UsageLevelResolver.Normalize(usedPercent), durationMinutes, resetsAt);
    }

    internal static bool TryReadTimestamp(JsonElement element, out DateTimeOffset value)
    {
        value = default;
        try
        {
            if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out var number))
            {
                // Accept Unix seconds and, defensively, Unix milliseconds.
                value = number > 100_000_000_000
                    ? DateTimeOffset.FromUnixTimeMilliseconds((long)number)
                    : DateTimeOffset.FromUnixTimeSeconds((long)number);
                return true;
            }

            if (element.ValueKind == JsonValueKind.String)
            {
                var text = element.GetString();
                if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var timestamp))
                {
                    value = timestamp > 100_000_000_000
                        ? DateTimeOffset.FromUnixTimeMilliseconds(timestamp)
                        : DateTimeOffset.FromUnixTimeSeconds(timestamp);
                    return true;
                }

                return DateTimeOffset.TryParse(
                    text,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal,
                    out value);
            }
        }
        catch (ArgumentOutOfRangeException)
        {
            // Out-of-range timestamps are treated as unknown.
        }

        return false;
    }
}
