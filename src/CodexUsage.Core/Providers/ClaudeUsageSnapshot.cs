using System.Text.Json;
using System.Text.Json.Serialization;
using CodexUsage.Core.Formatting;
using CodexUsage.Core.Models;
using CodexUsage.Core.Protocol;

namespace CodexUsage.Core.Providers;

public sealed record ClaudeSnapshotWindow(
    [property: JsonPropertyName("usedPercent")] double UsedPercent,
    [property: JsonPropertyName("resetsAt")] DateTimeOffset? ResetsAt);

/// <param name="ProcessIds">The relay's ancestors, nearest first, up to the Windows shell.</param>
/// <param name="OwnerProcessId">The Claude Code process; the host is valid only while it runs.</param>
public sealed record ClaudeSnapshotHost(
    [property: JsonPropertyName("sessionId")] string SessionId,
    [property: JsonPropertyName("processIds")] int[] ProcessIds,
    [property: JsonPropertyName("seenAt")] DateTimeOffset SeenAt,
    [property: JsonPropertyName("ownerProcessId")] int? OwnerProcessId = null);

/// <summary>
/// The local file written by the Claude Code status-line relay and read by the overlay.
/// It holds plan-limit percentages and the process chain of each recent Claude Code
/// session, so the rail can recognise the terminal or editor window hosting it.
/// </summary>
public sealed record ClaudeUsageSnapshot(
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("updatedAt")] DateTimeOffset? UpdatedAt,
    [property: JsonPropertyName("fiveHour")] ClaudeSnapshotWindow? FiveHour,
    [property: JsonPropertyName("sevenDay")] ClaudeSnapshotWindow? SevenDay,
    [property: JsonPropertyName("hosts")] ClaudeSnapshotHost[] Hosts)
{
    public const int CurrentVersion = 1;
    public const int MaximumHosts = 16;
    public static readonly TimeSpan HostLifetime = TimeSpan.FromHours(24);
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(15);

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = false };

    public static ClaudeUsageSnapshot Empty { get; } = new(CurrentVersion, null, null, null, []);

    public bool HasLimits => FiveHour is not null || SevenDay is not null;

    public string Serialize() => JsonSerializer.Serialize(this, SerializerOptions);

    public static bool TryDeserialize(string json, out ClaudeUsageSnapshot snapshot)
    {
        snapshot = Empty;
        if (string.IsNullOrWhiteSpace(json) || json.Length > ClaudeStatusLineParser.MaximumInputLength)
        {
            return false;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<ClaudeUsageSnapshot>(json, SerializerOptions);
            if (parsed is null || parsed.Version != CurrentVersion)
            {
                return false;
            }

            snapshot = parsed with { Hosts = parsed.Hosts ?? [] };
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Folds one status-line invocation into the stored snapshot.</summary>
    public ClaudeUsageSnapshot Apply(
        ClaudeRateLimits limits,
        string? sessionId,
        IReadOnlyCollection<int> processIds,
        DateTimeOffset now,
        int? ownerProcessId = null)
    {
        var hosts = (Hosts ?? [])
            .Where(host => now - host.SeenAt < HostLifetime && host.SeenAt <= now.AddMinutes(5))
            .Where(host => !string.Equals(host.SessionId, sessionId, StringComparison.Ordinal))
            .ToList();
        var ids = processIds.Where(id => id > 0).Distinct().Take(32).ToArray();
        if (ids.Length > 0)
        {
            hosts.Add(new ClaudeSnapshotHost(sessionId ?? "unknown", ids, now, ownerProcessId is > 0 ? ownerProcessId : null));
        }

        var updated = this with
        {
            Version = CurrentVersion,
            Hosts = hosts.OrderByDescending(host => host.SeenAt).Take(MaximumHosts).ToArray()
        };

        // A session without plan limits (for example before its first reply) must not
        // erase the last account-wide reading written by another session.
        return limits.HasAny
            ? updated with
            {
                UpdatedAt = now,
                FiveHour = ToWindow(limits.FiveHour),
                SevenDay = ToWindow(limits.SevenDay)
            }
            : updated;
    }

    /// <param name="isRunning">Checks that a session's Claude Code process still exists.</param>
    public bool IsHostProcess(uint processId, DateTimeOffset now, Func<int, bool> isRunning) =>
        processId > 0 && (Hosts ?? []).Any(host =>
            now - host.SeenAt < HostLifetime &&
            host.ProcessIds.Contains((int)processId) &&
            (host.OwnerProcessId is not { } owner || isRunning(owner)));

    public ProviderUsageState ToState(DateTimeOffset now)
    {
        if (!HasLimits || UpdatedAt is not { } updatedAt)
        {
            return ProviderUsageState.Unavailable(
                UsageProviderKind.Claude,
                ProviderConnection.Connecting,
                "Waiting",
                "Send a message in Claude Code in a terminal. Limits appear after its reply and need a Pro or Max sign-in.");
        }

        var windows = new List<UsageWindowDisplay>(2);
        AddWindow(windows, "claude:five_hour", "5-hour limit", FiveHour, ClaudeStatusLineParser.FiveHourMinutes, now);
        AddWindow(windows, "claude:seven_day", "Weekly limit", SevenDay, ClaudeStatusLineParser.SevenDayMinutes, now);
        var isStale = now - updatedAt > StaleAfter;
        return new ProviderUsageState(
            UsageProviderKind.Claude,
            isStale ? ProviderConnection.Stale : ProviderConnection.Live,
            RelativeTimeFormatter.FormatAge(updatedAt, now),
            isStale ? "Updates when Claude Code replies again." : string.Empty,
            UsageWindowOrdering.MostConstrainedFirst(windows),
            updatedAt);
    }

    private static void AddWindow(
        List<UsageWindowDisplay> windows,
        string key,
        string label,
        ClaudeSnapshotWindow? window,
        int durationMinutes,
        DateTimeOffset now)
    {
        if (window is null)
        {
            return;
        }

        // Once a window's reset time has passed, its allowance is full again.
        var hasReset = window.ResetsAt is { } resetsAt && resetsAt <= now;
        windows.Add(new UsageWindowDisplay(
            key,
            label,
            new QuotaWindow(
                hasReset ? 0 : UsageLevelResolver.Normalize(window.UsedPercent),
                durationMinutes,
                hasReset ? null : window.ResetsAt)));
    }

    private static ClaudeSnapshotWindow? ToWindow(QuotaWindow? window) =>
        window is null ? null : new ClaudeSnapshotWindow(window.UsedPercent, window.ResetsAt);
}
