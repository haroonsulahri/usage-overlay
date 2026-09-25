using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using UsageOverlay.Infrastructure;

namespace UsageOverlay.Services;

public sealed class ClaudeUsageStatusLineIntegration
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _directory;
    private readonly string _settingsPath;
    private readonly string _snapshotPath;
    private readonly string _scriptPath;
    private readonly string _powershellScriptPath;
    private readonly string _backupPath;

    public ClaudeUsageStatusLineIntegration(AppLogger logger)
        : this(
            Path.GetDirectoryName(logger.Path) ??
            throw new InvalidOperationException("The application data directory is unavailable."),
            ResolveClaudeConfigDirectory())
    {
    }

    public ClaudeUsageStatusLineIntegration(string localDirectory, string claudeConfigDirectory)
    {
        _directory = Path.GetFullPath(localDirectory);
        _snapshotPath = Path.Combine(_directory, "claude-usage.json");
        _scriptPath = Path.Combine(_directory, "claude-statusline.sh");
        _powershellScriptPath = Path.Combine(_directory, "ClaudeUsageStatusLine.ps1");
        _backupPath = Path.Combine(_directory, "claude-statusline-backup.json");

        _settingsPath = Path.Combine(Path.GetFullPath(claudeConfigDirectory), "settings.json");
    }

    public string SnapshotPath => _snapshotPath;

    public ClaudeIntegrationResult SetEnabled(bool enabled)
    {
        try
        {
            return enabled ? Enable() : Disable();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return new ClaudeIntegrationResult(false, $"Could not update Claude Code settings: {exception.Message}");
        }
    }

    private ClaudeIntegrationResult Enable()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        var settings = ReadSettings();
        var currentStatusLine = settings["statusLine"]?.DeepClone();
        var command = GetStatusLineCommand(currentStatusLine);
        var managedCommand = BuildCommand(_scriptPath);

        if (string.Equals(command, managedCommand, StringComparison.OrdinalIgnoreCase) && File.Exists(_scriptPath))
        {
            return new ClaudeIntegrationResult(true, "Claude Code status-line integration is connected.");
        }

        if (File.Exists(_backupPath))
        {
            return new ClaudeIntegrationResult(
                false,
                "Claude Code’s status line changed after setup. Disconnect the existing Usage Overlay integration before reconnecting.");
        }

        var previousCommand = currentStatusLine is null ? string.Empty : GetRequiredStatusLineCommand(currentStatusLine);
        if (previousCommand.Length > 8_192)
        {
            return new ClaudeIntegrationResult(false, "The existing Claude Code status-line command is too long to preserve safely.");
        }

        WritePowerShellScript();
        WriteStatusLineScript(previousCommand);

        var backup = new JsonObject
        {
            ["hadStatusLine"] = currentStatusLine is not null,
            ["statusLine"] = currentStatusLine,
            ["managedCommand"] = managedCommand
        };
        WriteAtomically(_backupPath, backup.ToJsonString(JsonOptions));

        var statusLine = currentStatusLine as JsonObject is { } existing
            ? (JsonObject)existing.DeepClone()
            : new JsonObject();
        statusLine["type"] = "command";
        statusLine["command"] = managedCommand;
        statusLine["refreshInterval"] ??= 60;
        settings["statusLine"] = statusLine;
        try
        {
            WriteSettings(settings);
        }
        catch
        {
            DeleteIfExists(_backupPath);
            throw;
        }

        return new ClaudeIntegrationResult(
            true,
            currentStatusLine is null
                ? "Claude Code connected. Usage appears after its first response."
                : "Claude Code connected. Your previous status line output is retained.");
    }

    private ClaudeIntegrationResult Disable()
    {
        if (!File.Exists(_backupPath))
        {
            return new ClaudeIntegrationResult(true, "Claude Code integration is already off.");
        }

        var backup = JsonNode.Parse(File.ReadAllText(_backupPath)) as JsonObject ??
                     throw new JsonException("The saved Claude status-line backup is invalid.");
        var managedCommand = backup["managedCommand"]?.GetValue<string>() ?? string.Empty;
        var settings = ReadSettings();
        if (!string.Equals(GetStatusLineCommand(settings["statusLine"]), managedCommand, StringComparison.OrdinalIgnoreCase))
        {
            return new ClaudeIntegrationResult(
                false,
                "Claude Code’s status line changed after setup. Restore or remove the Usage Overlay command in Claude settings first.");
        }

        if (backup["hadStatusLine"]?.GetValue<bool>() == true)
        {
            settings["statusLine"] = backup["statusLine"]?.DeepClone();
        }
        else
        {
            settings.Remove("statusLine");
        }

        WriteSettings(settings);
        File.Delete(_backupPath);
        DeleteIfExists(_scriptPath);
        DeleteIfExists(_powershellScriptPath);
        DeleteIfExists(_snapshotPath);
        return new ClaudeIntegrationResult(true, "Claude Code integration disconnected and your previous status line restored.");
    }

    private JsonObject ReadSettings()
    {
        if (!File.Exists(_settingsPath))
        {
            return new JsonObject();
        }

        return JsonNode.Parse(File.ReadAllText(_settingsPath)) as JsonObject ??
               throw new JsonException("Claude Code settings must contain a JSON object.");
    }

    private void WriteSettings(JsonObject settings) =>
        WriteAtomically(_settingsPath, settings.ToJsonString(JsonOptions));

    private void WritePowerShellScript()
    {
        var source = ReadEmbeddedScript("UsageOverlay.Services.ClaudeUsageStatusLine.ps1");
        WriteAtomically(_powershellScriptPath, source);
    }

    private void WriteStatusLineScript(string previousCommand)
    {
        var scriptPath = ToBashPath(_powershellScriptPath);
        var snapshotPath = ToBashPath(_snapshotPath);
        var script = $$"""
            #!/usr/bin/env bash
            input="$(cat)"
            if [[ -z "$input" || ${#input} -gt 1048576 ]]; then exit 0; fi
            usage_output="$(printf '%s' "$input" | powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File {{BashQuote(scriptPath)}} -SnapshotPath {{BashQuote(snapshotPath)}} 2>/dev/null)"
            previous_command={{BashQuote(previousCommand)}}
            previous_output=""
            if [[ -n "$previous_command" ]]; then
                previous_output="$(printf '%s' "$input" | eval "$previous_command" 2>/dev/null)"
            fi
            if [[ -n "$previous_output" && -n "$usage_output" ]]; then
                printf '%s | %s\n' "$previous_output" "$usage_output"
            elif [[ -n "$previous_output" ]]; then
                printf '%s\n' "$previous_output"
            elif [[ -n "$usage_output" ]]; then
                printf '%s\n' "$usage_output"
            fi
            """;
        WriteAtomically(_scriptPath, script);
    }

    private static string ReadEmbeddedScript(string resourceName)
    {
        using var stream = typeof(ClaudeUsageStatusLineIntegration).Assembly.GetManifestResourceStream(resourceName) ??
                           throw new InvalidOperationException("The Claude status-line relay is missing from this build.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static string ResolveClaudeConfigDirectory()
    {
        var configDirectory = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        return string.IsNullOrWhiteSpace(configDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude")
            : configDirectory;
    }

    private static string BuildCommand(string scriptPath) =>
        $"bash \"{ToBashPath(scriptPath)}\"";

    private static string ToBashPath(string path) =>
        Path.GetFullPath(path).Replace('\\', '/');

    private static string BashQuote(string value) =>
        "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";

    private static string? GetStatusLineCommand(JsonNode? statusLine) =>
        statusLine is JsonObject value && value["command"] is JsonValue command && command.TryGetValue<string>(out var text)
            ? text
            : null;

    private static string GetRequiredStatusLineCommand(JsonNode statusLine) =>
        GetStatusLineCommand(statusLine) ??
        throw new InvalidOperationException("The existing Claude Code status line has no command to preserve.");

    private static void WriteAtomically(string path, string contents)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temporaryPath, path, overwrite: true);
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}

public sealed record ClaudeIntegrationResult(bool Success, string Message);
