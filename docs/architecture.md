# Architecture

## Overview

Usage Overlay is a Windows-only companion process. It does not modify the Codex desktop package or render inside Codex's DOM.

```text
Codex services
      │
      │ existing Codex authentication
      ▼
codex app-server --stdio
      │
      │ JSON-RPC / JSONL
      ▼
AppServerClient ──► RateLimitParser ──► UsageSnapshot
                                             │
                                             ▼
CodexWindowLocator ───────────────────► WPF overlay
```

## Projects

### `CodexUsage.Core`

Platform-neutral logic:

- Rate-limit payload parsing
- Percentage normalization and severity thresholds
- Reset-time formatting
- Persistent display settings model
- Log redaction

### `UsageOverlay`

Windows integration:

- App Server process lifecycle and JSON-RPC transport
- Active Codex window detection
- WPF rail, detail card, animation, menus, and drag behavior
- Native Settings window and appearance application
- Settings persistence and Windows shortcuts
- Single-instance signaling for overlay visibility and Settings requests

## Data flow

1. The overlay locates `codex.cmd` or `codex.exe` on `PATH`, or uses `USAGE_OVERLAY_CODEX_PATH` when configured. The legacy `QUOTARAIL_CODEX_PATH` and `CODEX_USAGE_CODEX_PATH` variables are also accepted.
2. It starts `codex app-server --stdio` with redirected standard streams.
3. It sends `initialize`, `initialized`, and `account/rateLimits/read`.
4. It listens for `account/rateLimits/updated` and polls every 60 seconds as a fallback.
5. The parser clamps percentages, validates timestamps, and selects the primary `codex` bucket.
6. The UI calculates remaining quota as `100 - usedPercent` and animates to the new value.

## Window behavior

The overlay matches the Microsoft Store Codex package by process path and hides unless that window is foreground and visible. It uses per-monitor DPI scaling and clamps custom positions inside the active Codex window.

The WPF tool window is topmost and non-activating. It can receive pointer input without taking keyboard focus from Codex.

## Local state

```text
%LOCALAPPDATA%\UsageOverlay\settings.json
%LOCALAPPDATA%\UsageOverlay\overlay.log
```

Settings contain placement, offsets, fullscreen preference, and pause expiry. Logs contain redacted, truncated status and error messages.

Additional settings control Codex-only visibility, monitor following, remaining-versus-used display, colour thresholds, animation, compact labels, CLI path, and polling interval. Existing settings files remain compatible because missing fields receive bounded defaults. On first launch, Usage Overlay imports settings from `%LOCALAPPDATA%\QuotaRail\settings.json` or the older `%LOCALAPPDATA%\CodexUsageOverlay\settings.json` location when no Usage Overlay settings file exists.

## Failure handling

- App Server restarts after an unexpected exit.
- Missing or unsupported authentication is shown as a connection status.
- Malformed payloads are ignored without changing the last valid snapshot.
- Invalid settings fall back to bounded defaults.
- A second application launch signals the running instance instead of creating a duplicate.
