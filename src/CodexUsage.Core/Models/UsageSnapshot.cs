namespace CodexUsage.Core.Models;

public sealed record UsageSnapshot(
    RateLimitBucket Primary,
    IReadOnlyList<RateLimitBucket> Additional,
    DateTimeOffset RetrievedAt)
{
    public UsageSnapshot MergePartial(UsageSnapshot incoming)
    {
        if (incoming.Additional.Count > 0)
        {
            return incoming;
        }

        if (string.Equals(Primary.Id, incoming.Primary.Id, StringComparison.OrdinalIgnoreCase))
        {
            return incoming with { Additional = Additional };
        }

        var updated = Additional.ToList();
        var index = updated.FindIndex(bucket =>
            string.Equals(bucket.Id, incoming.Primary.Id, StringComparison.OrdinalIgnoreCase));

        if (index >= 0)
        {
            updated[index] = incoming.Primary;
        }
        else
        {
            updated.Add(incoming.Primary);
        }

        return this with { Additional = updated, RetrievedAt = incoming.RetrievedAt };
    }
}

