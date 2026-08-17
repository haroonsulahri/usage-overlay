using System.Text.Json;
using System.IO;
using CodexUsage.Core.Settings;

namespace CodexUsageOverlay.Infrastructure;

public sealed class OverlaySettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly AppLogger _logger;
    private readonly string _path;

    public OverlaySettingsStore(AppLogger logger)
    {
        _logger = logger;
        var directory = System.IO.Path.GetDirectoryName(logger.Path) ??
                        throw new InvalidOperationException("The application log path has no directory.");
        _path = System.IO.Path.Combine(directory, "settings.json");
    }

    public string Path => _path;

    public OverlaySettings Load()
    {
        if (!File.Exists(_path))
        {
            return new OverlaySettings();
        }

        try
        {
            var json = File.ReadAllText(_path);
            return (JsonSerializer.Deserialize<OverlaySettings>(json) ?? new OverlaySettings()).Normalize();
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.Error($"Could not load display settings: {exception.Message}");
            return new OverlaySettings();
        }
    }

    public void Save(OverlaySettings settings)
    {
        var normalized = settings.Normalize();
        var temporaryPath = _path + ".tmp";

        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(normalized, SerializerOptions));
            File.Move(temporaryPath, _path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Error($"Could not save display settings: {exception.Message}");
        }
    }
}
