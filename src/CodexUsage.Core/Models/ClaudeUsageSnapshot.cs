namespace CodexUsage.Core.Models;

public sealed record ClaudeUsageWindow(
    double UsedPercent,
    DateTimeOffset ResetsAt);

public sealed record ClaudeUsageSnapshot(
    ClaudeUsageWindow? FiveHour,
    ClaudeUsageWindow? SevenDay,
    DateTimeOffset UpdatedAt,
    int[] ProcessIds);
