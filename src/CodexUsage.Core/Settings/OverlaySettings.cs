namespace CodexUsage.Core.Settings;

public enum HorizontalPlacement
{
    RightEdge,
    LeftEdge,
    Custom
}

public sealed record OverlaySettings
{
    public HorizontalPlacement Placement { get; init; } = HorizontalPlacement.RightEdge;

    public double HorizontalOffset { get; init; }

    public double VerticalOffset { get; init; }

    public double CustomXRatio { get; init; } = 0.8;

    public double CustomYRatio { get; init; } = 0.75;

    public bool HideInFullscreen { get; init; }

    public DateTimeOffset? PausedUntil { get; init; }

    public OverlaySettings Normalize()
    {
        var placement = Enum.IsDefined(Placement)
            ? Placement
            : HorizontalPlacement.RightEdge;

        return this with
        {
            Placement = placement,
            HorizontalOffset = Math.Clamp(HorizontalOffset, -600, 600),
            VerticalOffset = Math.Clamp(VerticalOffset, -300, 600),
            CustomXRatio = Math.Clamp(CustomXRatio, 0, 1),
            CustomYRatio = Math.Clamp(CustomYRatio, 0, 1)
        };
    }

    public bool IsPaused(DateTimeOffset now) => PausedUntil is { } pausedUntil && pausedUntil > now;
}
