using System.Text.Json;
using System.IO;
using CodexUsage.Core.Settings;

namespace UsageOverlay.Infrastructure;

public sealed class OverlaySettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly AppLogger _logger;
    private readonly string _path;
    private readonly string[] _legacyPaths;

    public OverlaySettingsStore(AppLogger logger)
    {
        _logger = logger;
        var directory = System.IO.Path.GetDirectoryName(logger.Path) ??
                        throw new InvalidOperationException("The application log path has no directory.");
        _path = System.IO.Path.Combine(directory, "settings.json");
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _legacyPaths =
        [
            System.IO.Path.Combine(localAppData, "QuotaRail", "settings.json"),
            System.IO.Path.Combine(localAppData, "CodexUsageOverlay", "settings.json")
        ];
    }

    public string Path => _path;

    public OverlaySettings Load()
    {
        var legacyPath = _legacyPaths.FirstOrDefault(File.Exists);
        if (!File.Exists(_path) && legacyPath is not null)
        {
            try
            {
                var legacyJson = File.ReadAllText(legacyPath);
                var migrated = (JsonSerializer.Deserialize<OverlaySettings>(legacyJson) ?? new OverlaySettings()).Normalize();
                Save(migrated);
                if (File.Exists(_path))
                {
                    _logger.Info("Migrated settings from the previous application name.");
                }
                return migrated;
            }
            catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
            {
                _logger.Error($"Could not migrate previous display settings: {exception.Message}");
            }
        }

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
