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

Local files may be created under `%LOCALAPPDATA%\UsageOverlay`:

- `settings.json` stores display preferences.
- `overlay.log` stores redacted and truncated diagnostic messages.

Neither file is uploaded by this application. After opting in, `reporting.json` stores a random installation ID and the last attempted reporting date. This identity survives upgrades and disabling/re-enabling reporting; deleting the local state may create a new reporting installation.

For backward compatibility, the first launch may read `%LOCALAPPDATA%\QuotaRail\settings.json` or the older `%LOCALAPPDATA%\CodexUsageOverlay\settings.json` file and copy its normalized settings into the Usage Overlay directory. Previous files are not deleted.

## Authentication

Authentication is owned by Codex CLI and Codex services. The overlay asks the supported Codex App Server `account/read` method to refresh account state, but it does not read authentication files, cookies, passwords, or API keys.

## Network access

The overlay does not open a network listener. The Codex App Server process uses the account's existing Codex connection to retrieve usage metadata.

When the user explicitly selects **Check for updates**, Usage Overlay makes one HTTPS request to the public GitHub Releases API for `haroonsulahri/usage-overlay`. The request contains the standard HTTP metadata required by GitHub and the installed Usage Overlay version in the user-agent. No settings, usage percentages, account data, logs, or identifiers are sent by Usage Overlay. It does not check for updates automatically.

## Telemetry

Installation and activity reporting is optional and disabled by default, including after upgrading. In Settings, enable **Share optional installation and activity reports** and choose Save to opt in. Turn it off and Save to cancel pending requests and stop future reports. A request already received by the server cannot be recalled. Demo mode never reports.

The app sends only a randomly generated installation ID and app version to `https://haroone.com/api/usage-overlay.php`. Haroone records the UTC receipt date/time. There is at most one attempt per UTC calendar day while the app is running, with a five-second timeout. Offline, rejected and failed requests do not interrupt the app and are not retried until another UTC day. No account identifiers, names, emails, credentials, conversations, quota data, logs, hardware fingerprint or operating-system details are sent. There is no analytics SDK, advertising or crash-reporting service.

Haroone stores a keyed hash of the installation ID, daily activity dates, first/last reporting timestamps and latest app version in its protected website database. Daily activity expires after 90 calendar days including today. Installation summaries expire after 90 days without a report. Only authorized website administrators see aggregate installation counts, activity and version adoption; no public statistics endpoint is provided. Counts represent reporting installations, not exact people or all downloads, and cannot identify uninstalls.

Like any HTTPS service, the receiving infrastructure sees the source IP address. The reporting application stores only a keyed source hash for abuse control, expiring after at most two UTC calendar days; it does not store raw IP addresses or user agents. Deployment disables access logging for this endpoint. Infrastructure security logs and backups may have separate operational retention. Previously received reports are not deleted immediately on opt-out; they expire as described above. Contact hello@haroone.com with questions.
