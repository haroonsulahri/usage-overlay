using System.Text.Json;
using CodexUsage.Core.Settings;

internal static class SettingsMigrationSpecs
{
    public static void Run()
    {
        var legacy = OverlaySettings.FromJson("""{"ClaudeDesktopUsageEnabled":true,"ClaudeCodeUsageEnabled":false,"CursorUsageEnabled":true,"Placement":2,"CustomXRatio":0.75}""");
        Check(legacy.ClaudeEnabled && legacy.ClaudeAccountUsageEnabled, "Desktop Claude opt-in survives upgrade.");
        Check(!legacy.ClaudeStatusLineEnabled, "A disabled status-line hook stays disabled.");
        Check(legacy.CursorEnabled && legacy.CursorAccountUsageEnabled, "Cursor saved-sign-in source survives upgrade.");
        Check(legacy.Placement == HorizontalPlacement.Custom && legacy.CustomXRatio == 0.75, "Placement survives migration.");
        var code = OverlaySettings.FromJson("""{"ClaudeCodeUsageEnabled":true}""");
        Check(code.ClaudeEnabled && code.ClaudeStatusLineEnabled && !code.ClaudeAccountUsageEnabled, "Code-only opt-in does not enable account access.");
        var explicitOff = OverlaySettings.FromJson("""{"ClaudeDesktopUsageEnabled":true,"ClaudeCodeUsageEnabled":true,"CursorUsageEnabled":true,"ClaudeEnabled":false,"ClaudeStatusLineEnabled":false,"ClaudeAccountUsageEnabled":false,"CursorEnabled":false,"CursorAccountUsageEnabled":false}""");
        Check(!explicitOff.ClaudeEnabled && !explicitOff.ClaudeStatusLineEnabled && !explicitOff.ClaudeAccountUsageEnabled && !explicitOff.CursorEnabled && !explicitOff.CursorAccountUsageEnabled, "Explicit current opt-outs win over legacy opt-ins.");
        var fresh = OverlaySettings.FromJson("{}");
        Check(!fresh.ClaudeEnabled && !fresh.CursorEnabled && !fresh.ClaudeAccountUsageEnabled && !fresh.CursorAccountUsageEnabled, "New installs stay opt-in.");
        Check(OverlaySettings.FromJson(JsonSerializer.Serialize(legacy)) == legacy, "Saved migrated settings round-trip unchanged.");
        var disabled = OverlaySettings.FromJson("""{"ClaudeDesktopUsageEnabled":false,"ClaudeCodeUsageEnabled":false,"CursorUsageEnabled":false}""");
        Check(!disabled.ClaudeEnabled && !disabled.CursorEnabled, "Legacy disabled providers remain off.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
