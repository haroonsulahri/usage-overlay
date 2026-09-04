using System.Text.Json;

namespace CodexUsage.Core.Protocol;

public enum CodexAccountState
{
    Unknown,
    SignedOut,
    SignedIn
}

public static class AccountStateParser
{
    public static bool TryParseReadResponse(JsonElement root, out CodexAccountState state)
    {
        state = CodexAccountState.Unknown;
        if (!root.TryGetProperty("result", out var result) ||
            !result.TryGetProperty("account", out var account))
        {
            return false;
        }

        state = account.ValueKind == JsonValueKind.Null
            ? CodexAccountState.SignedOut
            : CodexAccountState.SignedIn;
        return true;
    }

    public static bool TryParseUpdatedNotification(JsonElement root, out CodexAccountState state)
    {
        state = CodexAccountState.Unknown;
        if (!root.TryGetProperty("method", out var method) ||
            !string.Equals(method.GetString(), "account/updated", StringComparison.Ordinal) ||
            !root.TryGetProperty("params", out var parameters) ||
            !parameters.TryGetProperty("authMode", out var authMode))
        {
            return false;
        }

        state = authMode.ValueKind == JsonValueKind.Null
            ? CodexAccountState.SignedOut
            : CodexAccountState.SignedIn;
        return true;
    }
}
