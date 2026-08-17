namespace CodexUsageOverlay.Infrastructure;

public sealed record OverlayOptions(double? DemoPercent, bool StartExpanded, bool StartHidden)
{
    public static OverlayOptions Parse(IReadOnlyList<string> arguments)
    {
        double? demoPercent = null;
        var startExpanded = false;
        var startHidden = false;

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

            if (argument.StartsWith("--demo=", StringComparison.OrdinalIgnoreCase))
            {
                var raw = argument["--demo=".Length..];
                if (double.TryParse(raw, out var value))
                {
                    demoPercent = Math.Clamp(value, 0, 100);
                }
            }
        }

        return new OverlayOptions(demoPercent, startExpanded, startHidden);
    }
}
