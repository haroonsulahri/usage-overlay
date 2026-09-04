using System.Net.Http.Json;
using System.Text.Json;

namespace CodexUsage.Core.Reporting;

/// <summary>Optional installation reporting, independent of all account and quota data.</summary>
public sealed class UsageReporter
{
    private readonly HttpClient _client;
    private readonly Uri _endpoint;
    private readonly string _statePath;
    private readonly string _version;
    private readonly Func<DateTimeOffset> _now;

    public UsageReporter(HttpClient client, Uri endpoint, string statePath, string version,
        Func<DateTimeOffset>? now = null)
    {
        _client = client;
        _endpoint = endpoint;
        _statePath = statePath;
        _version = version;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    // The caller serializes calls and cancels this token immediately when consent is withdrawn.
    public async Task ReportAsync(bool enabled, CancellationToken cancellationToken)
    {
        if (!enabled || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        try
        {
            var state = File.Exists(_statePath)
                ? JsonSerializer.Deserialize<ReportingState>(await File.ReadAllTextAsync(_statePath, cancellationToken).ConfigureAwait(false))
                : new ReportingState(Guid.NewGuid(), null);
            // Corrupt state fails closed rather than silently creating another installation.
            if (state is null || state.InstallationId == Guid.Empty)
            {
                return;
            }
            var today = DateOnly.FromDateTime(_now().UtcDateTime);
            if (state.LastAttemptDay is { } lastDay && lastDay >= today)
            {
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var temporaryPath = _statePath + ".tmp";
            await File.WriteAllTextAsync(temporaryPath,
                JsonSerializer.Serialize(state with { LastAttemptDay = today }), cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, _statePath, overwrite: true);
            // Persist before sending, so failures and restarts cannot cause a request storm.
            using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
            {
                Content = JsonContent.Create(new { installationId = state.InstallationId, appVersion = _version })
            };
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or JsonException or HttpRequestException or OperationCanceledException)
        {
            // Best effort only. Never log the installation ID, payload, or response.
        }
    }

    private sealed record ReportingState(Guid InstallationId, DateOnly? LastAttemptDay);
}
