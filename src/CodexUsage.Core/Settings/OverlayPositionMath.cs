namespace CodexUsage.Core.Settings;

public static class OverlayPositionMath
{
    public static double CalculateCustomTop(
        double anchorY,
        double currentHeight,
        double collapsedHeight,
        double verticalOffset)
    {
        var expansionHeight = Math.Max(0, currentHeight - collapsedHeight);
        return anchorY - collapsedHeight / 2 - expansionHeight - verticalOffset;
    }

    public static double CalculateCustomAnchorY(
        double windowTop,
        double currentHeight,
        double collapsedHeight)
    {
        return windowTop + currentHeight - collapsedHeight / 2;
    }
}
