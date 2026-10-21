using CodexUsage.Core.Models;

namespace CodexUsage.Core.Providers;

public static class CodexStatusMapper
{
    public const string Disconnected = "Disconnected";

    public static ProviderUsageState FromStatus(string status) => status switch
    {
        "CLI not found" => ProviderUsageState.Unavailable(
            UsageProviderKind.Codex, ProviderConnection.Unavailable, status, "CLI not found. Check Settings."),
        "Couldn’t connect" or "Trying again…" => ProviderUsageState.Unavailable(
            UsageProviderKind.Codex, ProviderConnection.Unavailable, status, "Couldn’t load limits. Retrying…"),
        "Signed out" => ProviderUsageState.Unavailable(
            UsageProviderKind.Codex, ProviderConnection.SignedOut, status,
            "Sign in to Codex. Usage will refresh automatically."),
        Disconnected => ProviderUsageState.Unavailable(
            UsageProviderKind.Codex, ProviderConnection.Disconnected, status,
            "Reconnect when the CLI update is finished."),
        _ => ProviderUsageState.Connecting(UsageProviderKind.Codex, "Connecting to Codex…")
    };

    public static ProviderUsageState FromSnapshot(UsageSnapshot snapshot) =>
        new(
            UsageProviderKind.Codex,
            ProviderConnection.Live,
            "Live",
            string.Empty,
            UsageWindowOrdering.MostConstrainedFirst(
                UsageWindowDisplayBuilder.Build(snapshot)
                    .Select(row => row with { Key = "codex:" + row.Key })
                    .ToArray()),
            snapshot.RetrievedAt);
}
