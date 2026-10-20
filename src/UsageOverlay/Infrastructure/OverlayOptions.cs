using CodexUsage.Core.Providers;

namespace UsageOverlay.Infrastructure;

public sealed record OverlayOptions(
    double? DemoPercent,
    bool StartExpanded,
    bool StartHidden,
    bool StartSettings,
    UsageProviderKind? DemoProvider = null)
{
    public static OverlayOptions Parse(IReadOnlyList<string> arguments)
    {
        double? demoPercent = null;
        var startExpanded = false;
        var startHidden = false;
        var startSettings = false;
        UsageProviderKind? demoProvider = null;

        foreach (var argument in arguments)
        {
            if (string.Equals(argument, "--expanded", StringComparison.OrdinalIgnoreCase))
            {
                startExpanded = true;
                continue;
            }

            if (string.Equals(argument, "--start-hidden", StringComparison.OrdinalIgnoreCase))
            {
                startHidden = true;
                continue;
            }

            if (string.Equals(argument, "--settings", StringComparison.OrdinalIgnoreCase))
            {
                startSettings = true;
                continue;
            }

            if (argument.StartsWith("--provider=", StringComparison.OrdinalIgnoreCase) &&
                Enum.TryParse<UsageProviderKind>(argument["--provider=".Length..], ignoreCase: true, out var provider) &&
                Enum.IsDefined(provider))
            {
                demoProvider = provider;
                continue;
            }

            if (argument.StartsWith("--demo=", StringComparison.OrdinalIgnoreCase))
            {
                var raw = argument["--demo=".Length..];
                if (double.TryParse(raw, out var value))
                {
                    demoPercent = Math.Clamp(value, 0, 100);
                }
            }
        }

        return new OverlayOptions(demoPercent, startExpanded, startHidden, startSettings, demoProvider);
    }
}
