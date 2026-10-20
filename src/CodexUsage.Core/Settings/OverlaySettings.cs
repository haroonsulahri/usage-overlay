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

    /// <summary>Shows the rail only while a supported app (Codex, Claude or Cursor) is active.</summary>
    public bool ShowOnlyWhenCodexActive { get; init; } = true;

    public bool CodexEnabled { get; init; } = true;

    /// <summary>Tracks Claude usage. The sources below decide where readings come from.</summary>
    public bool ClaudeEnabled { get; init; }

    /// <summary>Official source: Claude Code's status line reports plan limits after each reply.</summary>
    public bool ClaudeStatusLineEnabled { get; init; } = true;

    /// <summary>Unofficial source: the usage endpoint behind Claude's usage page, using Claude Code's saved sign-in.</summary>
    public bool ClaudeAccountUsageEnabled { get; init; }

    /// <summary>Reads Cursor team spend through the Admin API. Experimental.</summary>
    public bool CursorEnabled { get; init; }

    public string CursorAccountEmail { get; init; } = string.Empty;

    /// <summary>Unofficial source: Cursor's dashboard usage, using the editor's saved sign-in.</summary>
    public bool CursorAccountUsageEnabled { get; init; }

    public bool FollowCodexAcrossMonitors { get; init; } = true;

    public PrimaryUsageDisplay PrimaryDisplay { get; init; } = PrimaryUsageDisplay.Remaining;

    public double WarningThreshold { get; init; } = 70;

    public double CriticalThreshold { get; init; } = 90;

    public bool AnimationsEnabled { get; init; } = true;

    public bool ShowCompactPercentage { get; init; } = true;

    /// <summary>Lists the other enabled apps in the detail card. Off: each app shows only its own usage.</summary>
    public bool ShowOtherApps { get; init; }

    public bool NotificationsEnabled { get; init; }

    public bool UsageReportingEnabled { get; init; }

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

        var cursorEmail = (CursorAccountEmail ?? string.Empty).Trim();
        if (cursorEmail.Length > 254)
        {
            cursorEmail = cursorEmail[..254];
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
            CursorAccountEmail = cursorEmail,
            RefreshIntervalSeconds = Math.Clamp(RefreshIntervalSeconds, 15, 3_600)
        };
    }

    public bool IsPaused(DateTimeOffset now) => PausedUntil is { } pausedUntil && pausedUntil > now;
}
