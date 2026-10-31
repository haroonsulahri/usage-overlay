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

    public OverlaySettingsStore(AppLogger logger) : this(logger, AppPaths.SettingsPath,
        [System.IO.Path.Combine(AppPaths.DataDirectory, "settings.json"),
         System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QuotaRail", "settings.json"),
         System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexUsageOverlay", "settings.json")])
    {
    }

    internal OverlaySettingsStore(AppLogger logger, string path, string[] legacyPaths)
    {
        _logger = logger;
        _path = path;
        _legacyPaths = legacyPaths;
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
                var migrated = OverlaySettings.FromJson(legacyJson);
                Write(migrated, overwrite: false);
                _logger.Info("Migrated settings to the shared user-profile location.");
                return OverlaySettings.FromJson(File.ReadAllText(_path));
            }
            catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
            {
                // Another launch may have completed migration first. Its canonical file wins.
                if (File.Exists(_path)) return Read();
                _logger.Error($"Could not migrate previous display settings: {exception.Message}");
            }
        }

        if (!File.Exists(_path))
        {
            return new OverlaySettings();
        }

        return Read();
    }

    private OverlaySettings Read()
    {
        try
        {
            var json = File.ReadAllText(_path);
            return OverlaySettings.FromJson(json);
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.Error($"Could not load display settings: {exception.Message}");
            return new OverlaySettings();
        }
    }

    public void Save(OverlaySettings settings)
    {
        try
        {
            Write(settings, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Error($"Could not save display settings: {exception.Message}");
        }
    }

    private void Write(OverlaySettings settings, bool overwrite)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
        var temporaryPath = $"{_path}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings.Normalize(), SerializerOptions));
            File.Move(temporaryPath, _path, overwrite);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}
