using System.Text.Json;
using CodexUsage.Core.Providers;
using UsageOverlay.Services;

internal static class ClaudeSignInSpecs
{
    public static async Task RunAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "usage-overlay-signin-spec-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, ".credentials.json");
        try
        {
            using var client = new ClaudeAccountUsageClient(path);
            var missing = await client.FetchAsync(CancellationToken.None);
            Check(missing.Connection == ProviderConnection.SignedOut && missing.StatusText == "Sign-in required",
                "Missing credentials request sign-in rather than reporting a connection error");

            var saved = JsonSerializer.Serialize(new
            {
                claudeAiOauth = new { accessToken = "test-secret-do-not-display", expiresAt = DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeMilliseconds() }
            });
            await File.WriteAllTextAsync(path, saved);
            var expired = await client.FetchAsync(CancellationToken.None);
            Check(expired.Connection == ProviderConnection.SignedOut && expired.StatusText == "Sign-in expired" && expired.Limits is null,
                "Expired credentials surface an explicit sign-in state without calling the API");
            Check(expired.Problem!.Contains("claude auth login", StringComparison.Ordinal) &&
                  !expired.Problem.Contains("test-secret", StringComparison.Ordinal),
                "Recovery instructions provide the sign-in command without exposing credentials");
            Check(await File.ReadAllTextAsync(path) == saved, "Usage checks leave Claude credentials unchanged");
            await File.WriteAllTextAsync(path, "{broken");
            var malformed = await client.FetchAsync(CancellationToken.None);
            Check(malformed.Connection == ProviderConnection.SignedOut && malformed.Limits is null,
                "Malformed credentials produce a recoverable sign-in state");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
        Console.WriteLine("PASS " + label);
    }
}
