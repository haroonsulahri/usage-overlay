using System.IO;
using System.Text.Json;
using CodexUsage.Core.Models;
using CodexUsage.Core.Protocol;

namespace UsageOverlay.Services;

public sealed class ClaudeUsageClient
{
    private static readonly TimeSpan MaximumSnapshotAge = TimeSpan.FromMinutes(3);
    private readonly string _snapshotPath;

    public ClaudeUsageClient(string snapshotPath) => _snapshotPath = snapshotPath;

    public bool TryReadSnapshot(out ClaudeUsageSnapshot? snapshot)
    {
        snapshot = null;
        try
        {
            var json = File.ReadAllText(_snapshotPath);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!root.TryGetProperty("received_at", out var receivedAtElement) ||
                !receivedAtElement.TryGetDateTimeOffset(out var receivedAt) ||
                DateTimeOffset.UtcNow - receivedAt > MaximumSnapshotAge ||
                receivedAt > DateTimeOffset.UtcNow.AddMinutes(1))
            {
                return false;
            }

            var processIds = root.TryGetProperty("process_ids", out var processIdsElement) &&
                             processIdsElement.ValueKind == JsonValueKind.Array
                ? processIdsElement.EnumerateArray()
                    .Where(item => item.TryGetInt32(out _))
                    .Select(item => item.GetInt32())
                    .Where(id => id > 0)
                    .Distinct()
                    .Take(32)
                    .ToArray()
                : Array.Empty<int>();

            return ClaudeUsageParser.TryParseStatusLine(json, receivedAt, processIds, out snapshot);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }
}
