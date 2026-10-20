namespace CodexUsage.Core.Models;

public static class UsageWindowOrdering
{
    /// <summary>
    /// Puts the window with the least allowance left first, so the rail answers "how much
    /// can I still use?" even when a longer window runs out before a shorter one.
    /// The original order breaks ties.
    /// </summary>
    public static IReadOnlyList<UsageWindowDisplay> MostConstrainedFirst(IReadOnlyList<UsageWindowDisplay> windows) =>
        windows
            .Select((window, index) => (window, index))
            .OrderByDescending(item => UsageLevelResolver.Normalize(item.window.Window.UsedPercent))
            .ThenBy(item => item.index)
            .Select(item => item.window)
            .ToArray();
}
