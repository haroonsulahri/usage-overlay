using System.Text.Json;
using CodexUsage.Core.Models;

namespace CodexUsage.Core.Protocol;

public static class RateLimitParser
{
    private static readonly char[] IdSeparators = ['_', '-'];

    public static bool TryParse(string json, out UsageSnapshot? snapshot)
    {
        snapshot = null;

        try
        {
            using var document = JsonDocument.Parse(json);
            return TryParse(document.RootElement, out snapshot);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static bool TryParse(JsonElement root, out UsageSnapshot? snapshot)
    {
        snapshot = null;
        var container = Unwrap(root);

        if (container.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var buckets = new List<RateLimitBucket>();

        if (container.TryGetProperty("rateLimitsByLimitId", out var map) &&
            map.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in map.EnumerateObject())
            {
                if (TryParseBucket(property.Value, property.Name, out var bucket))
                {
                    buckets.Add(bucket);
                }
            }
        }

        if (buckets.Count == 0 &&
            container.TryGetProperty("rateLimits", out var single) &&
            TryParseBucket(single, "codex", out var singleBucket))
        {
            buckets.Add(singleBucket);
        }

        if (buckets.Count == 0 && TryParseBucket(container, "codex", out var directBucket))
        {
            buckets.Add(directBucket);
        }

        if (buckets.Count == 0)
        {
            return false;
        }

        var primary = buckets.FirstOrDefault(bucket =>
            string.Equals(bucket.Id, "codex", StringComparison.OrdinalIgnoreCase)) ?? buckets[0];
        var additional = buckets.Where(bucket => !ReferenceEquals(bucket, primary)).ToArray();

        snapshot = new UsageSnapshot(primary, additional, DateTimeOffset.UtcNow);
        return true;
    }

    private static JsonElement Unwrap(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return root;
        }

        if (root.TryGetProperty("result", out var result))
        {
            return result;
        }

        if (root.TryGetProperty("params", out var parameters))
        {
            return parameters;
        }

        return root;
    }

    private static bool TryParseBucket(
        JsonElement element,
        string fallbackId,
        out RateLimitBucket bucket)
    {
        bucket = null!;

        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty("primary", out var primaryElement) ||
            !TryParseWindow(primaryElement, out var primary))
        {
            return false;
        }

        var id = ReadString(element, "limitId") ?? fallbackId;
        var displayName = ReadString(element, "limitName") ?? HumanizeId(id);
        var planType = ReadString(element, "planType");
        QuotaWindow? secondary = null;

        if (element.TryGetProperty("secondary", out var secondaryElement) &&
            secondaryElement.ValueKind == JsonValueKind.Object &&
            TryParseWindow(secondaryElement, out var parsedSecondary))
        {
            secondary = parsedSecondary;
        }

        bucket = new RateLimitBucket(id, displayName, primary, secondary, planType);
        return true;
    }

    private static bool TryParseWindow(JsonElement element, out QuotaWindow window)
    {
        window = null!;

        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty("usedPercent", out var usedPercentElement) ||
            !usedPercentElement.TryGetDouble(out var usedPercent))
        {
            return false;
        }

        int? duration = null;
        if (element.TryGetProperty("windowDurationMins", out var durationElement) &&
            durationElement.TryGetInt32(out var parsedDuration))
        {
            duration = parsedDuration;
        }

        DateTimeOffset? resetsAt = null;
        if (element.TryGetProperty("resetsAt", out var resetElement) &&
            resetElement.TryGetInt64(out var unixSeconds))
        {
            try
            {
                resetsAt = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
            }
            catch (ArgumentOutOfRangeException)
            {
                resetsAt = null;
            }
        }

        window = new QuotaWindow(UsageLevelResolver.Normalize(usedPercent), duration, resetsAt);
        return true;
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static string HumanizeId(string id)
    {
        if (string.Equals(id, "codex", StringComparison.OrdinalIgnoreCase))
        {
            return "All Codex models";
        }

        return string.Join(
            ' ',
            id.Split(IdSeparators, StringSplitOptions.RemoveEmptyEntries)
                .Select(word => char.ToUpperInvariant(word[0]) + word[1..]));
    }
}
