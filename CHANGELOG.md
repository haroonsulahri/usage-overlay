# Changelog

## [0.4.1] - 2026-10-21

### Fixed

- Preserve legacy Claude and Cursor enabled settings and saved-sign-in sources when upgrading. Explicit current settings, including opt-outs, take precedence.
- Show Claude's 5-hour limit on the rail when available; keep weekly usage in the detail card and retain the weekly-only fallback. Cursor and Codex limit selection is unchanged.
- Bundle official Codex and Cursor application icons instead of generic badges.

## [0.4.0] - 2026-10-20

### Added

- Claude support: the rail appears beside the Claude desktop app and beside terminals and editors running Claude Code, showing the 5-hour and weekly plan limits that Claude Code passes to its status line. It's opt-in, keeps an existing status line working, and restores it when turned off or uninstalled.
- Experimental Cursor support through Cursor's official team Admin API, with the key stored encrypted with Windows DPAPI.
- Opt-in unofficial sources: Claude usage from Claude Code's saved sign-in (works when only the desktop app is used), and Cursor plan usage from the editor's saved sign-in (works with individual plans).
- An optional **Other apps** section in the detail card, and a tray tooltip summarising every enabled app.
- An **Apps** section in Settings to turn Codex, Claude and Cursor on or off.
- `--provider=` demo option for previewing each app.

### Changed

- The rail follows whichever supported app is in front, and leads with the limit closest to running out.
- The detail card sizes itself to its content.
- Visibility, fullscreen, monitor and theme settings now apply to every supported app.
- Diagnostic logs also redact `key_`-prefixed API keys.

## [0.3.1] - 2026-09-08

### Fixed

- Detect external Codex credential changes and restart the overlay-owned connection automatically after logout, login, or an account switch.
- Stop forcing OAuth token refreshes during routine usage polling.
- Reject invalidated usage replies, clear old account data during reconnect, and recover stalled requests after 30 seconds.
- Add isolated process-level logout/login and direct account-switch regression checks to the build.
- Observe credential-file metadata only, without reading or writing authentication contents.

## 0.3.0

- Add optional installation and activity reporting, disabled by default for new and existing users.
- Send only a random installation ID and app version at most once per UTC day, with bounded failures and cancellation on opt-out.
- Document reporting consent, privacy boundaries and retention.


All notable changes will be documented in this file.

The project follows [Semantic Versioning](https://semver.org/).

## [0.2.1] - 2026-09-04

### Fixed

- Refresh account state before reading usage so Codex sign-in, sign-out, and account switching are detected automatically
- Prefer the working CLI bundled with Codex Desktop instead of getting stuck on a broken `codex.cmd` earlier on `PATH`
- Clear usage from the previous account while Codex is signed out

## [0.2.0] - 2026-08-31

### Added

- Complete quota details for the primary and secondary windows of the main Codex limit
- Optional Windows notifications when any displayed limit first crosses the configured amber or red threshold
- Manual GitHub release checking from the rail and system-tray menus
- A disconnect and reconnect control that releases the Codex CLI process before a CLI upgrade
- A compact View usage link at the bottom of the detail card

### Changed

- Expanded details resize to fit the available usage windows while the collapsed rail keeps its original footprint
- Demo and live-smoke modes now exercise and report multiple usage windows
- App Server initialization now reports the real Usage Overlay assembly version

### Fixed

- Kept the overlay hidden on Codex-owned Open, Save, and file-picker dialogs instead of treating them as the main Codex window
- Kept a custom-positioned rail stationary while the detail card expands or collapses on hover
- Aligned the detail card's bottom edge with the full rail, including its compact percentage label

### Security

- Update checks are user-initiated and contact only the repository's public GitHub Releases API

## [0.1.1] - 2026-08-21

### Added

- Per-user Windows installer with Installed apps registration and a standard uninstaller
- Start Menu registration and automatic shortcut cleanup during uninstall
- Installer and portable archive checksums in every release

### Changed

- Made the installer the recommended download while keeping the portable zip available

### Fixed

- Added the missing Windows uninstall experience for public releases

## [0.1.0] - 2026-08-20

### Added

- Usage Overlay product identity and Haroone.com creator attribution
- Follow Codex, Dark, and Light themes with rendered Codex-theme detection and Windows fallback
- Clearer rail menu labels for hide, settings, refresh, and quit actions
- Live Codex quota rail driven by the documented App Server rate-limit methods
- Remaining percentage, reset countdown, and threshold colours
- Smooth GPU-safe animations with reduced-motion support
- Hover details, click-to-pin behavior, and freely draggable persistent placement
- Codex-only visibility, manual hide/show, pause, fullscreen exclusion, and automatic startup
- Single-instance recovery when the application is launched again
- Dark WPF and system-tray context menus
- Native Settings window with explicit Save and Cancel behavior
- Configurable visibility, thresholds, primary label, animation, compact percentage, CLI path, and refresh interval
- Accessible close button and Escape shortcut for pinned usage details
- Original multi-size Windows application icon for executable and shell surfaces
- Dependency-free specifications and authenticated live smoke test
- Redacted local diagnostic logging
- Windows release packaging with SHA-256 checksums

### Changed

- Renamed the visible Codex limits label to the clearer Codex usage wording
- Kept the hover card focused on the main Codex limit by removing the additional Spark row
- Reworked in-app labels, status messages, validation, and settings copy for clearer everyday language
- Made Windows Search launches open Settings while keeping automatic startup background-only
- Added a dedicated Visibility section and collapsed connection diagnostics by default
- Made Settings reliably open at the top instead of restoring a technical section near the bottom
- Replaced the indirect visibility checkbox with explicit Only in Codex and Across Windows choices

### Fixed

- Replaced stale quota values with clear loading and unavailable messages when Codex limits cannot be read
- Kept the primary Save action green on hover, with clear hover, pressed, focus, and disabled states
- Replaced Settings minimization with a minus action that hides only the Settings window
