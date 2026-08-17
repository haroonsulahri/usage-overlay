namespace CodexUsage.Core.Settings;

public enum AppTheme
{
    System,
    Dark,
    Light
}

public enum HorizontalPlacement
{
    RightEdge,
    LeftEdge,
    Custom
}

public enum PrimaryUsageDisplay
{
    Remaining,
    Used
}

public sealed record OverlaySettings
{
    public AppTheme Theme { get; init; } = AppTheme.System;

    public HorizontalPlacement Placement { get; init; } = HorizontalPlacement.RightEdge;

    public double HorizontalOffset { get; init; }

    public double VerticalOffset { get; init; }

    public double CustomXRatio { get; init; } = 0.8;

    public double CustomYRatio { get; init; } = 0.75;

    public bool HideInFullscreen { get; init; }

    public bool ShowOnlyWhenCodexActive { get; init; } = true;

    public bool FollowCodexAcrossMonitors { get; init; } = true;

    public PrimaryUsageDisplay PrimaryDisplay { get; init; } = PrimaryUsageDisplay.Remaining;

    public double WarningThreshold { get; init; } = 70;

    public double CriticalThreshold { get; init; } = 90;

    public bool AnimationsEnabled { get; init; } = true;

    public bool ShowCompactPercentage { get; init; } = true;

    public string CodexCliPath { get; init; } = string.Empty;

    public int RefreshIntervalSeconds { get; init; } = 60;

    public DateTimeOffset? PausedUntil { get; init; }

    public OverlaySettings Normalize()
    {
        var theme = Enum.IsDefined(Theme)
            ? Theme
            : AppTheme.System;

        var placement = Enum.IsDefined(Placement)
            ? Placement
            : HorizontalPlacement.RightEdge;

        var criticalThreshold = Math.Clamp(CriticalThreshold, 1, 100);
        var warningThreshold = Math.Clamp(WarningThreshold, 0, criticalThreshold - 1);
        var primaryDisplay = Enum.IsDefined(PrimaryDisplay)
            ? PrimaryDisplay
            : PrimaryUsageDisplay.Remaining;
        var cliPath = (CodexCliPath ?? string.Empty).Trim();
        if (cliPath.Length > 1_024)
        {
            cliPath = cliPath[..1_024];
        }

        return this with
        {
            Theme = theme,
            Placement = placement,
            HorizontalOffset = Math.Clamp(HorizontalOffset, -600, 600),
            VerticalOffset = Math.Clamp(VerticalOffset, -300, 600),
            CustomXRatio = Math.Clamp(CustomXRatio, 0, 1),
            CustomYRatio = Math.Clamp(CustomYRatio, 0, 1),
            PrimaryDisplay = primaryDisplay,
            WarningThreshold = warningThreshold,
            CriticalThreshold = criticalThreshold,
            CodexCliPath = cliPath,
            RefreshIntervalSeconds = Math.Clamp(RefreshIntervalSeconds, 15, 3_600)
        };
    }

    public bool IsPaused(DateTimeOffset now) => PausedUntil is { } pausedUntil && pausedUntil > now;
}
