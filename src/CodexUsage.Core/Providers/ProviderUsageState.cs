using CodexUsage.Core.Models;

namespace CodexUsage.Core.Providers;

public enum ProviderConnection
{
    Off,
    Connecting,
    Live,
    Stale,
    SignedOut,
    Unavailable,
    Disconnected
}

/// <summary>
/// One provider's latest usage, normalized for the rail. Windows are ordered with the
/// limit that should lead the rail first.
/// </summary>
public sealed record ProviderUsageState(
    UsageProviderKind Provider,
    ProviderConnection Connection,
    string StatusText,
    string Detail,
    IReadOnlyList<UsageWindowDisplay> Windows,
    DateTimeOffset? UpdatedAt,
    string? Summary = null)
{
    public bool HasUsage =>
        Windows.Count > 0 && Connection is ProviderConnection.Live or ProviderConnection.Stale;

    public UsageWindowDisplay? Lead => HasUsage ? Windows[0] : null;

    public string DisplayName => UsageProviderCatalog.DisplayName(Provider);

    public static ProviderUsageState Off(UsageProviderKind provider) =>
        Unavailable(provider, ProviderConnection.Off, "Off", "Turn this app on in Settings.");

    public static ProviderUsageState Connecting(UsageProviderKind provider, string detail) =>
        Unavailable(provider, ProviderConnection.Connecting, "Connecting…", detail);

    public static ProviderUsageState Unavailable(
        UsageProviderKind provider,
        ProviderConnection connection,
        string statusText,
        string detail) =>
        new(provider, connection, statusText, detail, Array.Empty<UsageWindowDisplay>(), null);
}
