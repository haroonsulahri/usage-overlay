# Changelog

All notable changes will be documented in this file.

The project follows [Semantic Versioning](https://semver.org/).

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
