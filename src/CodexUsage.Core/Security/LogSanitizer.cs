using System.Text.RegularExpressions;

namespace CodexUsage.Core.Security;

public static partial class LogSanitizer
{
    private const int MaximumMessageLength = 2_000;

    public static string Sanitize(string message)
    {
        var sanitized = message.ReplaceLineEndings(" ");
        sanitized = AuthorizationPattern().Replace(sanitized, "$1[REDACTED]");
        sanitized = BearerPattern().Replace(sanitized, "Bearer [REDACTED]");
        sanitized = NamedSecretPattern().Replace(sanitized, "$1[REDACTED]");
        sanitized = OpenAiKeyPattern().Replace(sanitized, "[REDACTED]");

        return sanitized.Length <= MaximumMessageLength
            ? sanitized
            : sanitized[..MaximumMessageLength] + " [TRUNCATED]";
    }

    [GeneratedRegex(
        "(?i)(authorization\\s*[:=]\\s*)(?:bearer\\s+)?([^\\s,;]+)",
        RegexOptions.CultureInvariant)]
    private static partial Regex AuthorizationPattern();

    [GeneratedRegex(
        "(?i)\\bbearer\\s+[A-Za-z0-9._~+/=-]+",
        RegexOptions.CultureInvariant)]
    private static partial Regex BearerPattern();

    [GeneratedRegex(
        "(?i)([\\\"']?(?:access_token|refresh_token|api_key|password|secret)[\\\"']?\\s*[:=]\\s*[\\\"']?)([^\\\"',\\s}]+)",
        RegexOptions.CultureInvariant)]
    private static partial Regex NamedSecretPattern();

    [GeneratedRegex(
        "\\bsk-[A-Za-z0-9_-]{12,}\\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex OpenAiKeyPattern();
}

