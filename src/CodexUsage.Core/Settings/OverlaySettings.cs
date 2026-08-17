namespace CodexUsage.Core.Settings;

public enum HorizontalPlacement
{
    AvoidRightSidebar,
    RightEdge,
    LeftEdge
}

public sealed record OverlaySettings
{
    public HorizontalPlacement Placement { get; init; } = HorizontalPlacement.AvoidRightSidebar;

    public double HorizontalOffset { get; init; }

    public double VerticalOffset { get; init; }

    public bool HideInFullscreen { get; init; }

    public DateTimeOffset? PausedUntil { get; init; }

    public OverlaySettings Normalize()
    {
        var placement = Enum.IsDefined(Placement)
            ? Placement
            : HorizontalPlacement.AvoidRightSidebar;

        return this with
        {
            Placement = placement,
            HorizontalOffset = Math.Clamp(HorizontalOffset, -600, 600),
            VerticalOffset = Math.Clamp(VerticalOffset, -300, 600)
        };
    }

    public bool IsPaused(DateTimeOffset now) => PausedUntil is { } pausedUntil && pausedUntil > now;
}

