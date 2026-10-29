# Privacy

Usage Overlay is designed to keep its data surface narrow.

## Data read

For Codex, the overlay requests account rate-limit metadata:

- Used percentage
- Quota window duration
- Reset timestamp
- Limit identifier and optional display name
- Plan type when returned by the service

It does not request or inspect prompts, responses, files, repository content, browser activity, or conversation history.

For Claude (off by default), Claude Code passes a JSON description of the session to its status-line command. Usage Overlay's relay keeps only the 5-hour and weekly used percentages and reset times, the session ID, and the IDs of the Claude Code process and its parent processes. It discards everything else, including the working directory, model, cost and transcript path, without storing or logging it. Turning Claude on edits the `statusLine` entry of Claude Code's `settings.json`. A restore point is kept, and turning Claude off restores the previous entry.

For Cursor (off by default, experimental), the overlay sends the email you enter as a search term to Cursor's Admin API and reads that member's spend, spend limit and billing-cycle start.

Two unofficial sources are separately opt-in:

- **Claude sign-in:** reads the OAuth access token from Claude Code's `.credentials.json` and sends it only to `api.anthropic.com/api/oauth/usage`, every 5 minutes, to read the 5-hour and weekly utilization. Saved-sign-in changes are detected through file metadata within the existing two-second poll. Credentials are read-only unless the separate automatic-renewal option below is enabled.
- **Automatic Claude renewal:** off by default and separately enabled in Settings. On access-token expiry or one usage HTTP 401, sends the existing refresh token, client identifier and existing scopes only to `https://platform.claude.com/v1/oauth/token`. Saves the renewed access token, expiry and any rotated refresh token atomically in the same private `.credentials.json`, preserving unrelated fields and Windows access controls. Uses Claude Code's current and legacy refresh locks; another login's file update takes precedence. It never logs tokens, starts a CLI/browser, or sends a model prompt. Disabling the option prevents future renewals; an already-issued renewal is saved before stopping so its rotated token is not lost. Network failures and rate limits are retried; unusable renewal credentials require sign-in. Temporary files stay in the Claude configuration directory and are removed after use.
- **Cursor sign-in:** opens Cursor's `state.vscdb` read-only to read `cursorAuth/accessToken`, and sends it only to `cursor.com` (and, as a fallback, `api2.cursor.sh`) every 5 minutes to read plan usage. The token is not stored or logged.

## Data stored

Local files may be created under `%LOCALAPPDATA%\UsageOverlay`:

- `settings.json` stores display preferences.
- `overlay.log` stores redacted and truncated diagnostic messages.
- `claude-usage.json` (Claude on) stores Claude plan-limit percentages, reset times, session IDs and process IDs.
- `claude-statusline.json` (Claude on) stores the Claude Code status-line entry to restore.
- `cursor-admin-key.bin` (Cursor key saved) stores the Cursor Admin API key encrypted with Windows DPAPI for the current user. The key is never written to settings or logs.

Neither file is uploaded by this application. After opting in, `reporting.json` stores a random installation ID and the last attempted reporting date. This identity survives upgrades and disabling/re-enabling reporting; deleting the local state may create a new reporting installation.

For backward compatibility, the first launch may read `%LOCALAPPDATA%\QuotaRail\settings.json` or the older `%LOCALAPPDATA%\CodexUsageOverlay\settings.json` file and copy its normalized settings into the Usage Overlay directory. Previous files are not deleted.

## Authentication

Authentication is owned by Codex CLI and Codex services. The overlay asks the supported Codex App Server `account/read` method for account state without forcing an OAuth token refresh. It checks only the existence, timestamps and size of `CODEX_HOME/auth.json` (or the default user `.codex` directory) to detect a login saved by another Codex process. It never reads or writes credential-file contents, cookies, passwords, or API keys. The observed metadata stays in memory and is not logged or reported.

## Network access

The overlay does not open a network listener. The Codex App Server process uses the account's existing Codex connection to retrieve usage metadata. Claude support makes no network requests; Claude Code supplies the limits locally.

With Cursor on, Usage Overlay sends `POST https://api.cursor.com/teams/spend` at most every 15 minutes, or once a minute when you choose Refresh usage. The request carries the Admin API key as Basic authentication, the email as a search term, and the Usage Overlay version in the user-agent.

When the user explicitly selects **Check for updates**, Usage Overlay makes one HTTPS request to the public GitHub Releases API for `haroonsulahri/usage-overlay`. The request contains the standard HTTP metadata required by GitHub and the installed Usage Overlay version in the user-agent. No settings, usage percentages, account data, logs, or identifiers are sent by Usage Overlay. It does not check for updates automatically.

## Telemetry

Installation and activity reporting is optional and disabled by default, including after upgrading. In Settings, enable **Share optional installation and activity reports** and choose Save to opt in. Turn it off and Save to cancel pending requests and stop future reports. A request already received by the server cannot be recalled. Demo mode never reports.

The app sends only a randomly generated installation ID and app version to `https://haroone.com/api/usage-overlay.php`. Haroone records the UTC receipt date/time. There is at most one attempt per UTC calendar day while the app is running, with a five-second timeout. Offline, rejected and failed requests do not interrupt the app and are not retried until another UTC day. No account identifiers, names, emails, credentials, conversations, quota data, logs, hardware fingerprint or operating-system details are sent. There is no analytics SDK, advertising or crash-reporting service.

Haroone stores a keyed hash of the installation ID, daily activity dates, first/last reporting timestamps and latest app version in its protected website database. Daily activity expires after 90 calendar days including today. Installation summaries expire after 90 days without a report. Only authorized website administrators see aggregate installation counts, activity and version adoption; no public statistics endpoint is provided. Counts represent reporting installations, not exact people or all downloads, and cannot identify uninstalls.

Like any HTTPS service, the receiving infrastructure sees the source IP address. The reporting application stores only a keyed source hash for abuse control, expiring after at most two UTC calendar days; it does not store raw IP addresses or user agents. Deployment disables access logging for this endpoint. Infrastructure security logs and backups may have separate operational retention. Previously received reports are not deleted immediately on opt-out; they expire as described above. Contact hello@haroone.com with questions.
