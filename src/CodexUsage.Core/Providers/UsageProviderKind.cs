namespace CodexUsage.Core.Providers;

public enum UsageProviderKind
{
    Codex,
    Claude,
    Cursor
}

public static class UsageProviderCatalog
{
    public static IReadOnlyList<UsageProviderKind> All { get; } =
    [
        UsageProviderKind.Codex,
        UsageProviderKind.Claude,
        UsageProviderKind.Cursor
    ];

    public static string DisplayName(UsageProviderKind provider) => provider switch
    {
        UsageProviderKind.Claude => "Claude",
        UsageProviderKind.Cursor => "Cursor",
        _ => "Codex"
    };

    public static Uri UsagePage(UsageProviderKind provider) => provider switch
    {
        UsageProviderKind.Claude => new Uri("https://claude.ai/settings/usage"),
        UsageProviderKind.Cursor => new Uri("https://cursor.com/dashboard?tab=usage"),
        _ => new Uri("https://chatgpt.com/codex/settings/usage")
    };
}
