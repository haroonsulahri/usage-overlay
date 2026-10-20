using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using CodexUsage.Core.Formatting;
using CodexUsage.Core.Protocol;
using CodexUsage.Core.Providers;
using UsageOverlay.Infrastructure;

namespace UsageOverlay.Services;

/// <summary>
/// Experimental Cursor usage from the official Admin API (<c>POST /teams/spend</c>).
/// It needs a team admin API key and reports the configured member's spend this cycle.
/// </summary>
public sealed class CursorUsageProvider : UsageProvider
{
    private static readonly Uri SpendEndpoint = new("https://api.cursor.com/teams/spend");
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan MinimumRefreshGap = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan RepublishInterval = TimeSpan.FromSeconds(30);

    private readonly AppLogger _logger;
    private readonly CursorCredentialStore _credentials;
    private readonly HttpClient _client;
    private readonly SemaphoreSlim _refreshSignal = new(0, 1);
    private ProviderUsageState? _lastReading;
    private DateTimeOffset _lastFetch = DateTimeOffset.MinValue;

    public CursorUsageProvider(AppLogger logger, CursorCredentialStore credentials, string email)
        : base(UsageProviderKind.Cursor)
    {
        _logger = logger;
        _credentials = credentials;
        Email = email;
        _client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        _client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("UsageOverlay", AppVersion.Current));
    }

    public string Email { get; set; }

    public override Task RefreshAsync()
    {
        if (IsRunning && DateTimeOffset.UtcNow - _lastFetch >= MinimumRefreshGap && _refreshSignal.CurrentCount == 0)
        {
            _refreshSignal.Release();
        }

        return Task.CompletedTask;
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync().ConfigureAwait(false);
        _client.Dispose();
        _refreshSignal.Dispose();
    }

    protected override async Task RunAsync(CancellationToken cancellationToken)
    {
        _lastReading = null;
        while (!cancellationToken.IsCancellationRequested)
        {
            await FetchAsync(cancellationToken).ConfigureAwait(false);
            var nextFetch = DateTimeOffset.UtcNow + PollInterval;
            while (DateTimeOffset.UtcNow < nextFetch)
            {
                if (await _refreshSignal.WaitAsync(RepublishInterval, cancellationToken).ConfigureAwait(false))
                {
                    break;
                }

                if (_lastReading is { UpdatedAt: { } updatedAt } reading)
                {
                    Publish(reading with { StatusText = RelativeTimeFormatter.FormatAge(updatedAt, DateTimeOffset.UtcNow) });
                }
            }
        }
    }

    private async Task FetchAsync(CancellationToken cancellationToken)
    {
        var key = _credentials.Read();
        var email = Email.Trim();
        if (string.IsNullOrWhiteSpace(key) || email.Length == 0)
        {
            Publish(Unavailable("Set up", "Add your Cursor email and a team Admin API key in Settings."));
            return;
        }

        _lastFetch = DateTimeOffset.UtcNow;
        if (_lastReading is null)
        {
            Publish(ProviderUsageState.Connecting(UsageProviderKind.Cursor, "Reading Cursor team spend…"));
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, SpendEndpoint)
            {
                Content = JsonContent.Create(new { searchTerm = email, page = 1, pageSize = 100 })
            };
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes(key + ":")));
            using var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                _lastReading = null;
                Publish(Unavailable("Key rejected", "Cursor rejected the Admin API key. Check it in Settings."));
                _logger.Error($"Cursor Admin API returned {(int)response.StatusCode}.");
                return;
            }

            if (!response.IsSuccessStatusCode)
            {
                PublishFailure($"Cursor returned HTTP {(int)response.StatusCode}.");
                return;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!CursorSpendParser.TryParse(json, email, out var spend))
            {
                _lastReading = null;
                Publish(Unavailable("Not found", $"No Cursor team member matches {email}."));
                return;
            }

            _lastReading = CursorSpendParser.ToState(spend, DateTimeOffset.UtcNow);
            Publish(_lastReading);
            _logger.Info("Cursor usage updated.");
        }
        catch (Exception exception) when (
            (exception is HttpRequestException or TaskCanceledException) && !cancellationToken.IsCancellationRequested)
        {
            PublishFailure($"Couldn’t reach Cursor: {exception.Message}");
        }
    }

    private void PublishFailure(string logMessage)
    {
        _logger.Error(logMessage);
        Publish(_lastReading is { } last
            ? last with
            {
                Connection = ProviderConnection.Stale,
                Detail = "Couldn’t refresh. Showing the last reading."
            }
            : Unavailable("Couldn’t connect", "Couldn’t load Cursor usage. Retrying…"));
    }

    private static ProviderUsageState Unavailable(string status, string detail) =>
        ProviderUsageState.Unavailable(UsageProviderKind.Cursor, ProviderConnection.Unavailable, status, detail);
}
