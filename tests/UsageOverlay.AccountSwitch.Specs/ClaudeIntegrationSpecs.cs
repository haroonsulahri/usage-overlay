using System.Text.Json.Nodes;
using UsageOverlay.Services;

internal static class ClaudeIntegrationSpecs
{
    public static void Run()
    {
        ConnectsWithoutExistingStatusLine();
        WrapsAndRestoresExistingStatusLine();
        LeavesStatusLineTheUserChanged();
        RepairsCommandAfterExecutableMoves();
        RefusesUnreadableSettings();
        Console.WriteLine("All Claude Code integration checks passed.");
    }

    private static void ConnectsWithoutExistingStatusLine()
    {
        using var scope = new Scope();
        var integration = scope.Create(scope.Executable("Usage Overlay"));
        Assert(integration.Connect().Success, "Connects to a fresh Claude Code profile");
        var command = scope.ReadCommand();
        Assert(command!.EndsWith("--claude-statusline", StringComparison.Ordinal), "Uses the relay directly when nothing is wrapped");
        Assert(!command.Contains("--quiet", StringComparison.Ordinal), "Shows usage text when no status line existed");

        Assert(integration.Disconnect().Success, "Disconnects");
        Assert(scope.ReadSettings()["statusLine"] is null, "Removes the status line it added");
        Assert(!File.Exists(scope.StatePath), "Deletes the restore point");
    }

    private static void WrapsAndRestoresExistingStatusLine()
    {
        using var scope = new Scope();
        scope.WriteSettings("""
            {
              "model": "opus",
              "statusLine": { "type": "command", "command": "~/.claude/statusline.sh", "padding": 2 }
            }
            """);
        var integration = scope.Create(scope.Executable("Apps"));
        Assert(integration.Connect().Success, "Connects over an existing status line");
        var settings = scope.ReadSettings();
        var command = settings["statusLine"]!["command"]!.GetValue<string>();
        Assert(command.Contains("~/.claude/statusline.sh", StringComparison.Ordinal), "Keeps running the user's command");
        Assert(command.Contains("--quiet", StringComparison.Ordinal), "Keeps the user's output on its own");
        Assert(settings["statusLine"]!["padding"]!.GetValue<int>() == 2, "Preserves status-line options");
        Assert(settings["model"]!.GetValue<string>() == "opus", "Preserves unrelated settings");

        var connected = File.ReadAllText(scope.SettingsPath);
        Assert(integration.Connect().Success, "Reconnecting succeeds");
        Assert(File.ReadAllText(scope.SettingsPath) == connected, "Reconnecting is idempotent");

        Assert(integration.Disconnect().Success, "Disconnects");
        var restored = scope.ReadSettings()["statusLine"]!;
        Assert(restored["command"]!.GetValue<string>() == "~/.claude/statusline.sh", "Restores the original command");
        Assert(restored["padding"]!.GetValue<int>() == 2, "Restores the original options");
    }

    private static void LeavesStatusLineTheUserChanged()
    {
        using var scope = new Scope();
        var integration = scope.Create(scope.Executable("Apps"));
        Assert(integration.Connect().Success, "Connects");
        scope.WriteSettings("""{ "statusLine": { "type": "command", "command": "my-new-line" } }""");
        Assert(integration.Disconnect().Success, "Disconnects after a manual change");
        Assert(scope.ReadCommand() == "my-new-line", "Leaves a status line the user replaced");
        Assert(!File.Exists(scope.StatePath), "Clears the restore point");
    }

    private static void RepairsCommandAfterExecutableMoves()
    {
        using var scope = new Scope();
        scope.WriteSettings("""{ "statusLine": { "type": "command", "command": "echo original" } }""");
        Assert(scope.Create(scope.Executable("Old")).Connect().Success, "Connects from the old location");
        var moved = scope.Create(scope.Executable("New Place"));
        Assert(moved.Connect().Success, "Reconnects from the new location");
        var command = scope.ReadCommand()!;
        Assert(command.Contains("/New Place/UsageOverlay.exe", StringComparison.Ordinal), "Points at the moved executable");
        Assert(!command.Contains("/Old/", StringComparison.Ordinal), "Drops the old executable");
        Assert(command.Contains("echo original", StringComparison.Ordinal), "Still wraps the original command once");
        Assert(moved.Disconnect().Success, "Disconnects");
        Assert(scope.ReadCommand() == "echo original", "Restores the original after a move");
    }

    private static void RefusesUnreadableSettings()
    {
        using var scope = new Scope();
        scope.WriteSettings("{ not json");
        var result = scope.Create(scope.Executable("Apps")).Connect();
        Assert(!result.Success, "Refuses to rewrite malformed Claude settings");
        Assert(File.ReadAllText(scope.SettingsPath) == "{ not json", "Leaves malformed settings untouched");

        scope.WriteSettings("""{ "statusLine": { "type": "static", "text": "hi" } }""");
        Assert(!scope.Create(scope.Executable("Apps")).Connect().Success, "Refuses a status line it cannot wrap");
    }

    private static void Assert(bool condition, string label)
    {
        if (!condition)
        {
            throw new InvalidOperationException(label);
        }

        Console.WriteLine("PASS " + label);
    }

    private sealed class Scope : IDisposable
    {
        private readonly string _directory =
            Path.Combine(Path.GetTempPath(), "usage-overlay-claude-spec-" + Guid.NewGuid().ToString("N"));

        public Scope() => Directory.CreateDirectory(_directory);

        public string SettingsPath => Path.Combine(_directory, ".claude", "settings.json");

        public string StatePath => Path.Combine(_directory, "claude-statusline.json");

        public string Executable(string folder) => Path.Combine(_directory, folder, "UsageOverlay.exe");

        public ClaudeCodeIntegration Create(string executablePath) =>
            new(StatePath, Path.Combine(_directory, "claude-usage.json"), SettingsPath, executablePath);

        public void WriteSettings(string json)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, json);
        }

        public JsonObject ReadSettings() => (JsonObject)JsonNode.Parse(File.ReadAllText(SettingsPath))!;

        public string? ReadCommand() => ReadSettings()["statusLine"]?["command"]?.GetValue<string>();

        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }
}
