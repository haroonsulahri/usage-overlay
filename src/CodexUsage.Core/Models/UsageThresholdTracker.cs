using CodexUsage.Core;

namespace CodexUsage.Core.Models;

public sealed record UsageThresholdAlert(
    UsageWindowDisplay Window,
    UsageLevel Level);

public sealed class UsageThresholdTracker
{
    private readonly Dictionary<string, UsageLevel> _levels = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<UsageThresholdAlert> Evaluate(
        UsageSnapshot snapshot,
        double warningThreshold,
        double criticalThreshold)
    {
        var alerts = new List<UsageThresholdAlert>();

        foreach (var window in UsageWindowDisplayBuilder.Build(snapshot))
        {
            var level = UsageLevelResolver.FromPercentage(
                window.Window.UsedPercent,
                warningThreshold,
                criticalThreshold);
            if (_levels.TryGetValue(window.Key, out var previous) &&
                (int)level > (int)previous &&
                level != UsageLevel.Normal)
            {
                alerts.Add(new UsageThresholdAlert(window, level));
            }

            _levels[window.Key] = level;
        }

        return alerts;
    }
}
