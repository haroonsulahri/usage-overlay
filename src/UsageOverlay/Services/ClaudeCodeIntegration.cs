using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using UsageOverlay.Infrastructure;

namespace UsageOverlay.Services;

public sealed record ClaudeIntegrationResult(bool Success, string Message);

/// <summary>
/// Points Claude Code's status line at Usage Overlay's relay. An existing status-line
/// command keeps running and keeps its output; Usage Overlay only records the limits.
/// Disconnecting restores the previous status line unless the user has since changed it.
/// </summary>
public sealed class ClaudeCodeIntegration
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };
    private const int MaximumPreviousCommandLength = 8_192;

    private readonly string _statePath;
    private readonly string _settingsPath;
    private readonly string _executablePath;
    private readonly string _snapshotPath;

    public ClaudeCodeIntegration(
        string statePath,
        string snapshotPath,
        string claudeSettingsPath,
        string executablePath)
    {
        _statePath = statePath;
        _snapshotPath = snapshotPath;
        _settingsPath = claudeSettingsPath;
        _executablePath = Path.GetFullPath(executablePath);
    }

    public static ClaudeCodeIntegration CreateDefault() =>
        new(
            AppPaths.ClaudeIntegrationStatePath,
            AppPaths.ClaudeSnapshotPath,
            Path.Combine(ResolveClaudeConfigDirectory(), "settings.json"),
            Environment.ProcessPath ?? throw new InvalidOperationException("The executable path is unavailable."));

    public string SettingsPath => _settingsPath;

    public ClaudeIntegrationResult Connect()
    {
        try
        {
            return ConnectCore();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return new ClaudeIntegrationResult(false, $"Couldn’t update Claude Code settings: {exception.Message}");
        }
    }

    public ClaudeIntegrationResult Disconnect()
    {
        try
        {
            return DisconnectCore();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return new ClaudeIntegrationResult(false, $"Couldn’t restore Claude Code settings: {exception.Message}");
        }
    }

    private ClaudeIntegrationResult ConnectCore()
    {
        var settings = ReadSettings();
        var current = settings["statusLine"];
        var currentCommand = GetCommand(current);
        var state = ReadState();

        JsonNode? previous;
        if (state is not null && string.Equals(currentCommand, state.ManagedCommand, StringComparison.Ordinal))
        {
            // Already connected. Rebuild the command in case this executable moved.
            previous = state.PreviousStatusLine;
        }
        else
        {
            if (current is not null && currentCommand is null)
            {
                return new ClaudeIntegrationResult(
                    false,
                    "Claude Code uses a status line Usage Overlay can’t wrap. Remove it in Claude settings to connect.");
            }

            // First connection, or the user replaced the status line since: wrap theirs.
            previous = current?.DeepClone();
        }

        // Never wrap a relay command left by an earlier install whose restore point is gone.
        if (GetCommand(previous)?.Contains(ClaudeStatusLineRelay.Argument, StringComparison.Ordinal) == true)
        {
            previous = null;
        }

        var previousCommand = GetCommand(previous) ?? string.Empty;
        if (previousCommand.Length > MaximumPreviousCommandLength)
        {
            return new ClaudeIntegrationResult(false, "The existing Claude Code status-line command is too long to keep safely.");
        }

        var managedCommand = BuildCommand(_executablePath, previousCommand);
        if (state is not null &&
            string.Equals(currentCommand, managedCommand, StringComparison.Ordinal) &&
            string.Equals(state.ManagedCommand, managedCommand, StringComparison.Ordinal))
        {
            return new ClaudeIntegrationResult(true, ConnectedMessage(previousCommand));
        }

        var statusLine = previous is JsonObject existing ? (JsonObject)existing.DeepClone() : new JsonObject();
        statusLine["type"] = "command";
        statusLine["command"] = managedCommand;
        settings["statusLine"] = statusLine;

        // Save the restore point first so a failed settings write can always be undone.
        WriteState(new IntegrationState(managedCommand, previous?.DeepClone()));
        AppPaths.WriteAtomically(_settingsPath, settings.ToJsonString(WriteOptions));
        return new ClaudeIntegrationResult(true, ConnectedMessage(previousCommand));
    }

    private ClaudeIntegrationResult DisconnectCore()
    {
        var state = ReadState();
        if (state is null)
        {
            return new ClaudeIntegrationResult(true, "Claude Code isn’t connected.");
        }

        var settings = ReadSettings();
        var restored = false;
        if (string.Equals(GetCommand(settings["statusLine"]), state.ManagedCommand, StringComparison.Ordinal))
        {
            if (state.PreviousStatusLine is null)
            {
                settings.Remove("statusLine");
            }
            else
            {
                settings["statusLine"] = state.PreviousStatusLine.DeepClone();
            }

            AppPaths.WriteAtomically(_settingsPath, settings.ToJsonString(WriteOptions));
            restored = true;
        }

        File.Delete(_statePath);
        if (File.Exists(_snapshotPath))
        {
            File.Delete(_snapshotPath);
        }

        return new ClaudeIntegrationResult(
            true,
            restored
                ? "Claude Code disconnected. Your previous status line is restored."
                : "Claude Code disconnected. Your current status line was left unchanged.");
    }

    internal static string BuildCommand(string executablePath, string previousCommand)
    {
        var path = Path.GetFullPath(executablePath).Replace('\\', '/');
        var executable = BashQuote(path);
        if (string.IsNullOrWhiteSpace(previousCommand))
        {
            // Double quotes work in both bash and Command Prompt; fall back to bash
            // quoting only for paths containing characters bash expands inside them.
            var portable = path.IndexOfAny(['$', '`', '"']) < 0 ? $"\"{path}\"" : executable;
            return $"{portable} {ClaudeStatusLineRelay.Argument}";
        }

        // Claude Code runs status lines through bash. Feed the same input to both
        // commands and show only the user's own status-line output.
        return
            "input=\"$(cat)\"; " +
            $"printf '%s' \"$input\" | {executable} {ClaudeStatusLineRelay.Argument} {ClaudeStatusLineRelay.QuietArgument} >/dev/null 2>&1; " +
            "printf '%s' \"$input\" | (\n" + previousCommand + "\n)";
    }

    private static string ConnectedMessage(string previousCommand) =>
        previousCommand.Length == 0
            ? "Connected. Usage appears after Claude Code’s next reply."
            : "Connected. Your existing status line keeps working.";

    private JsonObject ReadSettings()
    {
        if (!File.Exists(_settingsPath))
        {
            return new JsonObject();
        }

        var text = File.ReadAllText(_settingsPath);
        if (string.IsNullOrWhiteSpace(text))
        {
            return new JsonObject();
        }

        return JsonNode.Parse(text, documentOptions: new JsonDocumentOptions
               {
                   AllowTrailingCommas = true,
                   CommentHandling = JsonCommentHandling.Skip
               }) as JsonObject ??
               throw new JsonException("Claude Code settings must contain a JSON object.");
    }

    private IntegrationState? ReadState()
    {
        if (!File.Exists(_statePath))
        {
            return null;
        }

        var node = JsonNode.Parse(File.ReadAllText(_statePath)) as JsonObject ??
                   throw new JsonException("The saved Claude Code restore point is invalid.");
        var managed = node["managedCommand"]?.GetValue<string>() ??
                      throw new JsonException("The saved Claude Code restore point is invalid.");
        return new IntegrationState(managed, node["previousStatusLine"]?.DeepClone());
    }

    private void WriteState(IntegrationState state)
    {
        var node = new JsonObject
        {
            ["managedCommand"] = state.ManagedCommand,
            ["previousStatusLine"] = state.PreviousStatusLine
        };
        AppPaths.WriteAtomically(_statePath, node.ToJsonString(WriteOptions));
    }

    private static string? GetCommand(JsonNode? statusLine) =>
        statusLine is JsonObject value &&
        value["command"] is JsonValue command &&
        command.TryGetValue<string>(out var text)
            ? text
            : null;

    private static string BashQuote(string value) =>
        "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    internal static string ResolveClaudeConfigDirectory()
    {
        var configured = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude")
            : configured;
    }

    private sealed record IntegrationState(string ManagedCommand, JsonNode? PreviousStatusLine);
}
