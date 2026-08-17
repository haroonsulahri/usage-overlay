# Privacy

Usage Overlay is designed to keep its data surface narrow.

## Data read

The overlay requests Codex account rate-limit metadata:

- Used percentage
- Quota window duration
- Reset timestamp
- Limit identifier and optional display name
- Plan type when returned by the service

It does not request or inspect prompts, responses, files, repository content, browser activity, or conversation history.

## Data stored

Two local files may be created under `%LOCALAPPDATA%\UsageOverlay`:

- `settings.json` stores display preferences.
- `overlay.log` stores redacted and truncated diagnostic messages.

Neither file is uploaded by this application.

For backward compatibility, the first launch may read `%LOCALAPPDATA%\QuotaRail\settings.json` or the older `%LOCALAPPDATA%\CodexUsageOverlay\settings.json` file and copy its normalized settings into the Usage Overlay directory. Previous files are not deleted.

## Authentication

Authentication is owned by Codex CLI and Codex services. The overlay does not read authentication files, cookies, passwords, or API keys.

## Network access

The overlay does not open a network listener or make its own HTTP requests. The Codex App Server process uses the account's existing Codex connection to retrieve usage metadata.

## Telemetry

The overlay contains no analytics, crash-reporting SDK, advertising, or telemetry endpoint.
