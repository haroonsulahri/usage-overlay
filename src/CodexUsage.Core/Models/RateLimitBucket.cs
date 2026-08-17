namespace CodexUsage.Core.Models;

public sealed record RateLimitBucket(
    string Id,
    string DisplayName,
    QuotaWindow Primary,
    QuotaWindow? Secondary,
    string? PlanType);

