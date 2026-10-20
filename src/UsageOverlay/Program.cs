using UsageOverlay.Services;

namespace UsageOverlay;

public static class Program
{
    public const string DisconnectClaudeArgument = "--disconnect-claude";

    [STAThread]
    public static int Main(string[] args)
    {
        // Claude Code runs this on every status-line update, so it must stay fast and
        // must not touch WPF, the tray icon or the single-instance mutex.
        if (args.Length > 0 && string.Equals(args[0], ClaudeStatusLineRelay.Argument, StringComparison.OrdinalIgnoreCase))
        {
            return ClaudeStatusLineRelay.Run(args);
        }

        // The uninstaller restores Claude Code's previous status line.
        if (args.Length > 0 && string.Equals(args[0], DisconnectClaudeArgument, StringComparison.OrdinalIgnoreCase))
        {
            return ClaudeCodeIntegration.CreateDefault().Disconnect().Success ? 0 : 1;
        }

        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }
}
