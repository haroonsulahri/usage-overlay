namespace CodexUsage.Core.Models;

public sealed record QuotaWindow(
    double UsedPercent,
    int? WindowDurationMinutes,
    DateTimeOffset? ResetsAt);

