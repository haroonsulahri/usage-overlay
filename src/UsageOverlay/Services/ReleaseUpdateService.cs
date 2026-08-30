using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using UsageOverlay.Infrastructure;

namespace UsageOverlay.Services;

public sealed record ReleaseCheckResult(
    bool IsUpdateAvailable,
    string CurrentVersion,
    string LatestVersion,
    Uri ReleaseUri);

public sealed class ReleaseUpdateService : IDisposable
{
    private static readonly Uri LatestReleaseApi =
        new("https://api.github.com/repos/haroonsulahri/usage-overlay/releases/latest");

    private readonly HttpClient _httpClient;

    public ReleaseUpdateService()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("UsageOverlay", AppVersion.Current));
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public async Task<ReleaseCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(LatestReleaseApi, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var root = document.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ??
                  throw new InvalidOperationException("The latest release has no version tag.");
        var releaseUrl = root.GetProperty("html_url").GetString() ??
                         throw new InvalidOperationException("The latest release has no web address.");
        var latestVersion = tag.Trim().TrimStart('v', 'V');
        var currentVersion = AppVersion.Current.Trim().TrimStart('v', 'V');

        if (!Version.TryParse(currentVersion, out var current) ||
            !Version.TryParse(latestVersion, out var latest))
        {
            throw new InvalidOperationException("The release version could not be compared.");
        }

        return new ReleaseCheckResult(
            latest > current,
            currentVersion,
            latestVersion,
            new Uri(releaseUrl));
    }

    public void Dispose() => _httpClient.Dispose();
}
