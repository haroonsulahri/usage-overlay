# Architecture

## Overview

Usage Overlay is a Windows-only companion process. It does not modify the Codex, Claude or Cursor apps or render inside them.

```text
codex app-server ──JSON-RPC──► AppServerClient ──► CodexUsageProvider ──┐
                                                                         │
Claude Code status line                                                  │  ProviderUsageState
  └► UsageOverlay.exe --claude-statusline                                ├─────────────────────► WPF rail
       └► claude-usage.json ──────────────────► ClaudeUsageProvider ─────┤        ▲
                                                                         │        │
api.cursor.com /teams/spend ───────────────────► CursorUsageProvider ────┘        │
                                                                                  │
HostWindowLocator (foreground Codex, Claude, Cursor or Claude Code window) ───────┘
```

Each provider turns its source into the same `ProviderUsageState`: a connection state, short status text, and an ordered list of limit windows. Claude places the 5-hour window first when available and retains weekly usage as a detail row; other providers retain their existing ordering. The rail shows the provider of the app in front; the card lists the others.

## Projects

### `CodexUsage.Core`

Platform-neutral logic:

- The provider model (`UsageProviderKind`, `ProviderUsageState`) and Codex status mapping
- Claude status-line parsing and the `claude-usage.json` snapshot model, including host-session tracking
- Cursor Admin API spend parsing
- Supported-app window identification
- Rate-limit payload parsing
- Percentage normalization and severity thresholds
- Reset-time formatting
- Persistent display settings model
- Log redaction

### `UsageOverlay`

Windows integration:

- `UsageProvider` background lifecycle shared by the Codex, Claude and Cursor providers
- App Server process lifecycle and JSON-RPC transport
- The Claude Code status-line relay (`Program.Main` handles `--claude-statusline` before WPF starts) and the reversible `~/.claude/settings.json` integration
- DPAPI storage for the Cursor Admin API key
- Active app window detection, including terminals and editors that host Claude Code
- WPF rail, detail card, animation, menus, and drag behavior
- Native Settings window and appearance application
- Settings persistence and Windows shortcuts
- Single-instance signaling for overlay visibility and Settings requests

## Data flow

### Codex

1. The overlay uses an explicitly configured CLI path first, then the Codex Desktop bundled CLI when available, and finally `codex.cmd` or `codex.exe` on `PATH`. The legacy `QUOTARAIL_CODEX_PATH` and `CODEX_USAGE_CODEX_PATH` variables are also accepted.
2. It starts `codex app-server --stdio` with redirected standard streams.
3. It sends `initialize`, `initialized`, `account/read` with `refreshToken: false`, and then `account/rateLimits/read` for a signed-in ChatGPT account. Codex retains responsibility for authentication refresh when needed.
4. It listens for `account/updated` and `account/rateLimits/updated`, and polls the account state every 60 seconds by default. Notifications trigger a correlated usage request; late responses invalidated by account changes are ignored.
5. It renders the primary and secondary windows for the main Codex limit while keeping model-specific buckets out of the interface.
6. Every two seconds, and before processing a response, it checks only the existence, creation/modification timestamps and size of the effective `CODEX_HOME/auth.json`. A change clears the displayed usage and renews the overlay-owned App Server session after a five-second delay. Credential contents are never read or written.
7. When that file is absent or inaccessible, the session is renewed every minute so a credential manager can be reloaded. Initialization and outstanding account/usage requests have a 30-second timeout. Logins held only in another process, and file replacements preserving all monitored metadata, are outside this detection mechanism.

The user can disconnect App Server from either context menu before upgrading an npm-installed Codex CLI. Disconnecting cancels the session and terminates only the process tree started by Usage Overlay. Reconnecting creates a fresh App Server session.

Update checks are separate from account usage. They run only after the user selects **Check for updates** and request the latest public release metadata from GitHub.
The parser clamps percentages, validates timestamps, and selects the primary `codex` bucket. The UI calculates remaining quota as `100 - usedPercent` and animates to the new value.

### Claude

1. Turning Claude on saves a restore point to `claude-statusline.json`, then sets Claude Code's `statusLine.command` to run `UsageOverlay.exe --claude-statusline`. An existing command is wrapped in a small bash script: both commands get the same input, and only the user's command's output is shown.
2. Claude Code runs the command on status-line updates. `Program.Main` recognises the argument and runs the relay without loading WPF or taking the single-instance mutex.
3. The relay parses `rate_limits`, walks its parent processes up to (not including) `explorer.exe` and other shell or system hosts, and identifies the Claude Code process (`claude.exe`, or `node.exe` for npm installs).
4. Under a named mutex it merges this into `claude-usage.json`. A session without `rate_limits` updates only its host entry, so it never erases the last account-wide reading. Host entries expire after 24 hours and are kept to 16.
5. `ClaudeUsageProvider` polls the file's metadata every two seconds and republishes every 30 seconds so ages and resets stay current. Readings older than 15 minutes are marked stale. A window whose reset time has passed is shown as reset.
6. The window locator treats a foreground window as a Claude host when its process is in a session's chain and that session's Claude Code process is still running.
7. Turning Claude off, or `--disconnect-claude` from the uninstaller, restores the previous status line, unless the user has replaced it since.

### Claude saved-sign-in renewal

The unofficial account source checks credential-file metadata on its existing two-second timer. A changed sign-in clears the in-memory reading and starts a new usage check; a response racing with another account's saved sign-in is discarded. Normal successful account polls remain five minutes apart.

Automatic renewal is separately disabled by default. When enabled, an expired access token or one usage HTTP 401 triggers a bounded refresh-token exchange at Anthropic's fixed token endpoint. Existing scopes are preserved. The client acquires the current `.oauth_refresh.lock` and legacy configuration-directory lock used by the installed Windows Claude Code implementation, rereads credentials, and defers to a concurrent sign-in. Only this app's own abandoned locks can be recovered, after their recorded process is proven dead and their directory instance still matches.

Renewed credentials preserve unrelated JSON fields, are flushed into a private temporary file and replace the original atomically with its Windows access controls. Once a rotating-token request is issued, its bounded save finishes even when the provider stops, so the new refresh token is not lost. No terminal, browser or model prompt is launched. Authentication rejection requires login; network failures and rate limits preserve credentials and back off. Credentials remain read-only when renewal is disabled.

### Cursor

`CursorUsageProvider` reads the DPAPI-protected key and calls `POST https://api.cursor.com/teams/spend` every 15 minutes, filtered by the configured email. Rejected keys and unknown members are reported in the card. Network failures keep the last reading, marked stale. With a spend limit (`hardLimitOverrideDollars` or `monthlyLimitDollars`), or `totalPercentUsed` when the response includes it, the rail shows a percentage. Otherwise the card shows the amount spent.

## Window behavior

The overlay matches the Microsoft Store Codex package by process path, the Claude desktop app and native Claude Code by `claude.exe`, Cursor by `Cursor.exe`, and Claude Code hosts by the recorded process chain. It hides unless a supported, enabled app's window is foreground and visible. It uses per-monitor DPI scaling and clamps custom positions inside the active Codex window.

The WPF tool window is topmost and non-activating. It can receive pointer input without taking keyboard focus from Codex.

## Local state

```text
%USERPROFILE%\.usage-overlay\settings.json
%LOCALAPPDATA%\UsageOverlay\overlay.log
```

Settings contain theme, placement, offsets, fullscreen preference, and pause expiry. Logs contain redacted, truncated status and error messages.

Additional settings control which apps are tracked (Codex, Claude, Cursor and the Cursor email), supported-app-only visibility, monitor following, Follow app/Dark/Light appearance, remaining-versus-used display, colour thresholds, animation, compact labels, CLI path, and polling interval. Follow app samples only a few active-window background pixels to infer light or dark; it does not store or log screen content. Existing settings files remain compatible because missing fields receive bounded defaults. On first launch, Usage Overlay imports settings from `%LOCALAPPDATA%\QuotaRail\settings.json` or the older `%LOCALAPPDATA%\CodexUsageOverlay\settings.json` location when no Usage Overlay settings file exists.

## Failure handling

- App Server restarts after an unexpected exit.
- Missing or unsupported authentication is shown as a connection status.
- Malformed payloads are ignored without changing the last valid snapshot.
- Invalid settings fall back to bounded defaults.
- A second application launch signals the running instance instead of creating a duplicate.

Settings are shared across ordinary desktop, packaged-parent and Windows Startup launches. On the first v0.4.4 launch, existing UsageOverlay AppData settings (or the older QuotaRail/CodexUsageOverlay settings) are migrated only if the shared settings file is absent. The first migrated file wins; conflicting old copies are not merged and explicit opt-outs remain authoritative. Original files are retained. Automatic Claude renewal remains off unless already enabled or explicitly selected. Logs, snapshots, encrypted keys and optional reporting identity keep their existing AppData locations.
