using System.Text.Json;
using CodexUsage.Core.Models;

namespace CodexUsage.Core.Protocol;

public static class ClaudeUsageParser
{
    public static bool TryParseStatusLine(
        string json,
        DateTimeOffset updatedAt,
        int[]? processIds,
        out ClaudeUsageSnapshot? snapshot)
    {
        snapshot = null;
        if (string.IsNullOrWhiteSpace(json) || json.Length > 1_048_576)
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

            var fiveHour = (ClaudeUsageWindow?)null;
            var sevenDay = (ClaudeUsageWindow?)null;
            if (root.TryGetProperty("rate_limits", out var rateLimits) &&
                rateLimits.ValueKind == JsonValueKind.Object)
            {
                fiveHour = ParseWindow(rateLimits, "five_hour");
                sevenDay = ParseWindow(rateLimits, "seven_day");
            }

            snapshot = new ClaudeUsageSnapshot(
                fiveHour,
                sevenDay,
                updatedAt,
                (processIds ?? Array.Empty<int>()).Where(id => id > 0).Distinct().Take(32).ToArray());
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static ClaudeUsageWindow? ParseWindow(JsonElement rateLimits, string propertyName)
    {
        if (!rateLimits.TryGetProperty(propertyName, out var element) ||
            element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty("used_percentage", out var usedElement) ||
            !usedElement.TryGetDouble(out var usedPercent) ||
            !double.IsFinite(usedPercent) ||
            !element.TryGetProperty("resets_at", out var resetsElement) ||
            !resetsElement.TryGetInt64(out var resetsUnixSeconds) ||
            resetsUnixSeconds <= 0)
        {
            return null;
        }

        return new ClaudeUsageWindow(
            Math.Clamp(usedPercent, 0, 100),
            DateTimeOffset.FromUnixTimeSeconds(resetsUnixSeconds));
    }
}
