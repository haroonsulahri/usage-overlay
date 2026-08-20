# Usage Overlay

<p>
  <img src="assets/icon.svg" width="88" height="88" alt="Usage Overlay icon">
</p>

**See your remaining Codex usage without leaving the desktop app.**

Usage Overlay is a small, open-source Windows companion for the Codex desktop app. It places a slim usage rail beside Codex and opens a compact detail card when you need the numbers. You can see how much usage is left and when the main Codex limit resets.

It runs as its own process. It does not modify Codex, inject code into the app, read conversations, or handle your credentials.

Built by [Haroone.com](https://haroone.com).

> [!IMPORTANT]
> Usage Overlay is an independent preview project. It is not affiliated with or endorsed by OpenAI.

## What it shows

The collapsed rail is designed to stay out of the way. Its fill and compact label show the amount remaining at a glance.

Hover over the rail to open the detail card. The card shows:

- Remaining or used percentage, depending on your preference
- The reset countdown for the current limit window
- Live, connecting, or unavailable connection status
- Green, amber, or red status based on your configured thresholds

If Codex has not returned a fresh limit yet, the rail shows `--`. The card says **Loading usage** while connecting and **Usage unavailable** if the limits cannot be read. It never presents an old percentage as a current reading after the connection is lost.

## Features

- **Remaining usage first:** the default view answers the useful question: how much do I have left?
- **Compact detail card:** hover to inspect the limit or click to keep the card open.
- **Reset countdown:** see when the current quota window resets.
- **Live updates:** listen for App Server updates and poll at a configurable interval as a fallback.
- **Three visual states:** green for normal usage, amber for warning, and red for critical.
- **Flexible placement:** use the right edge, left edge, position nudges, or drag the rail anywhere inside Codex.
- **Monitor-aware positioning:** follow Codex when the app moves between monitors.
- **Visibility control:** show the overlay only with Codex or keep it available across Windows.
- **Fullscreen control:** optionally hide the rail while Codex is fullscreen.
- **Theme support:** follow the active Codex light or dark surface, or force Light or Dark.
- **Reduced motion support:** turn animations off, with Windows reduced-motion settings respected automatically.
- **Native Settings window:** change behavior without editing configuration files.
- **System tray access:** reopen the rail, refresh usage, open Settings, view logs, or quit.
- **Automatic startup:** optionally start Usage Overlay when Windows starts.
- **Single instance:** opening the app again talks to the running process instead of creating a duplicate.
- **Local by design:** no analytics, advertising, telemetry, or remote account system.

Usage Overlay reads limits through the officially documented [`account/rateLimits/read`](https://developers.openai.com/codex/app-server) and `account/rateLimits/updated` Codex App Server methods.

## Requirements

- Windows 10 or Windows 11 on x64
- The Microsoft Store Codex desktop app
- Codex CLI installed and available on `PATH`
- Codex CLI signed in with ChatGPT-backed authentication

API-key-only and Bedrock authentication do not provide the account usage data this app displays.

## Install

### Download a release

1. Download the Windows zip from the repository's **Releases** page.
2. Extract the full zip to a directory you control.
3. Run `UsageOverlay.exe`.
4. Focus the Codex desktop app. The rail will appear at its saved position.

The app is not code-signed yet. Windows SmartScreen may show a warning on first launch. Review the published SHA-256 checksum before running the file.

### Add Windows shortcuts

The release includes optional PowerShell helpers:

```powershell
.\scripts\install-start-menu.ps1
.\scripts\install-startup.ps1
```

The Start-menu shortcut opens Settings. The startup shortcut launches the overlay quietly when you sign in to Windows.

Remove either shortcut without touching the application files:

```powershell
.\scripts\uninstall-start-menu.ps1
.\scripts\uninstall-startup.ps1
```

## Using the overlay

| Action | Result |
| --- | --- |
| Hover over the rail | Temporarily opens usage details |
| Move the pointer away | Closes an unpinned detail card |
| Click the rail | Pins or unpins usage details |
| Click the card's close button | Closes only the detail card |
| Press `Escape` | Closes pinned details without stopping the app |
| Drag the rail | Saves a custom position |
| Right-click the rail | Opens hide, refresh, Settings, and quit actions |
| Double-click the tray icon | Restores a manually hidden rail |
| Open Usage Overlay from Windows Search | Opens the native Settings window |
| Launch the executable again | Restores the existing instance; no duplicate is created |

Choosing **Hide usage rail** does not stop usage updates. Use the tray icon to show it again. Choose **Quit Usage Overlay** when you want to stop the process.

## Settings

Open Settings from **Open settings…** in the rail or tray menu. Opening **Usage Overlay** from Windows Search also goes straight to this window.

![Native Usage Overlay Settings window](docs/images/settings.png)

### Saving and closing

Most controls are staged while the window is open. Choose **Save** or press `Ctrl+S` to validate and write them to `%LOCALAPPDATA%\UsageOverlay\settings.json`.

- **Save:** applies valid changes and keeps the Settings window open. A confirmation appears beside the buttons.
- **Cancel:** closes Settings and discards changes made since the last save.
- **Escape:** works like Cancel.
- **Minus button:** hides only the Settings window. The rail and tray process keep running, and unsaved fields remain available when you reopen Settings.
- **Close button:** closes Settings, discards unsaved changes, and does not quit Usage Overlay.

Theme choices are previewed immediately so you can compare them. Cancel, Escape, or the close button restores the last saved theme.

Save is blocked when:

- The amber or red threshold is not a number
- A threshold is outside `0–100`
- The amber threshold is equal to or higher than the red threshold
- The refresh interval is outside `15–3600` seconds
- A custom Codex CLI path does not point to an existing file

The validation message appears at the bottom of the window and moves focus to the field that needs attention.

### General

- **Open when Windows starts:** adds or removes a shortcut in your Windows Startup folder. This is a user-level setting and does not install a service.

### Visibility

- **Only in Codex:** shows the rail while the Codex window is active and hides it when you switch away. This is the default.
- **Across Windows:** keeps the rail at its last position when you switch to another application.
- **Hide while Codex is fullscreen:** removes the rail only while the active Codex window fills its monitor.
- **Pause 15 min:** stages a 15-minute pause. Save to start the pause. Reopen Settings and choose **Resume now** to end it early.

Pausing or hiding the rail does not disconnect App Server. Usage can continue updating in the background.

### Position

- **Right edge:** anchors the rail to the right side of the tracked window.
- **Left edge:** anchors it to the left side.
- **Anywhere:** uses the custom position saved when you drag the rail.
- **X and Y buttons:** move the selected position 20 pixels with each click. The current offset is shown below the buttons.
- **Reset:** selects the right edge and returns both offsets to zero.
- **Follow Codex between monitors:** moves the overlay with Codex instead of keeping it on the monitor where the position was first established.

Custom positions are stored as relative coordinates, so the rail remains inside the Codex window after resizing or changing monitors.

### Appearance

- **Theme:** choose **Follow Codex**, **Dark**, or **Light**.
- **Main number:** choose **Left** to lead with remaining usage or **Used** to lead with consumed usage.
- **Turn amber at:** set the used percentage where the rail changes from green to amber.
- **Turn red at:** set the higher used percentage where the rail changes to red.
- **Animate changes:** smoothly moves the rail when a fresh value arrives. Windows reduced-motion preferences are still respected.
- **Show the percentage below the rail:** keeps the compact number visible while the detail card is closed.

**Follow Codex** checks a few background pixels from the active Codex window to decide whether the surface is light or dark. It does not capture, save, or log screen content. Windows appearance is used as a fallback when Codex is unavailable.

### Connection and diagnostics

- This section is collapsed by default because most users do not need it.
- **Codex CLI path:** optionally point directly to `codex.cmd` or `codex.exe` when the command is not available on `PATH`.
- **Refresh every:** sets the fallback polling interval from 15 to 3,600 seconds. Live notifications can still update the rail sooner.
- **Open logs:** opens the local redacted diagnostic log.
- **Restore defaults:** stages the original settings. Choose Save to keep them.

CLI path and refresh interval changes take effect after the app restarts.

### Default settings

| Setting | Default |
| --- | --- |
| Start with Windows | Off |
| Visibility | Only in Codex |
| Hide in fullscreen | Off |
| Position | Right edge |
| Follow Codex between monitors | On |
| Theme | Follow Codex |
| Main number | Left |
| Amber threshold | 70% used |
| Red threshold | 90% used |
| Animate changes | On |
| Percentage below rail | On |
| Custom CLI path | Empty; use `PATH` |
| Refresh interval | 60 seconds |

## Connection states

| State | What it means |
| --- | --- |
| `Connecting…` | The app is starting Codex App Server and waiting for a fresh limit. |
| `Live` | A current limit was received and updates are active. |
| `Couldn’t connect` | Codex returned an error. The app keeps trying. |
| `Trying again…` | App Server stopped or failed and will be restarted. |
| `CLI not found` | Set the Codex CLI path in Settings or add it to `PATH`. |

When no current limit is available, the rail remains neutral and shows `--` instead of a percentage.

## How it works

Usage Overlay starts:

```text
codex app-server --stdio
```

It communicates with App Server through newline-delimited JSON-RPC:

1. Start and initialize App Server.
2. Request current limits with `account/rateLimits/read`.
3. Listen for `account/rateLimits/updated` notifications.
4. Poll periodically as a fallback.
5. Normalize the response and calculate remaining usage as `100 - usedPercent`.
6. Update the WPF rail and detail card on the desktop.

The overlay identifies the active Microsoft Store Codex window by its package process path. Its WPF window is topmost and non-activating, so pointer controls work without taking keyboard focus from Codex.

See [Architecture](docs/architecture.md) for the component map, data flow, Windows behavior, and failure handling.

## Privacy

Usage Overlay requests only Codex rate-limit metadata:

- Used percentage
- Window duration
- Reset timestamp
- Limit identifier and display name
- Plan type when returned

It does not request prompts, responses, conversation history, repository files, browser activity, cookies, passwords, API keys, or Codex authentication files.

The app does not make its own HTTP requests. Codex CLI owns authentication and network access. Usage Overlay has no telemetry or analytics endpoint.

Read the full [Privacy policy](docs/privacy.md) and [Security policy](SECURITY.md) before sharing logs or reporting a vulnerability.

## Local files

Usage Overlay stores two files on your computer:

```text
%LOCALAPPDATA%\UsageOverlay\settings.json
%LOCALAPPDATA%\UsageOverlay\overlay.log
```

`settings.json` contains display preferences. `overlay.log` contains redacted and truncated connection messages. Neither file is uploaded by the app.

Older settings from `%LOCALAPPDATA%\QuotaRail` or `%LOCALAPPDATA%\CodexUsageOverlay` are imported once when no current settings file exists. The old files are left unchanged.

## Troubleshooting

### The rail does not appear

- Make sure Codex is open, visible, and focused.
- Open Usage Overlay again from Windows Search to restore a hidden instance.
- Check **Visibility** in Settings. Choose **Across Windows** if you do not want Codex-only behavior.
- Check whether the overlay is paused or hidden during fullscreen.
- Right-click the system-tray icon and choose **Show usage rail**.

### The card says `CLI not found`

Run this in PowerShell:

```powershell
codex --version
```

If Windows cannot find the command, install Codex CLI or provide its full path under **Settings > Connection and diagnostics**.

### The card says `Usage unavailable`

- Confirm that Codex CLI is signed in with ChatGPT-backed authentication.
- Choose **Refresh usage** from the rail or tray menu.
- Open the diagnostic log from Settings.
- Restart Usage Overlay after changing the CLI path.

The app retries connection failures automatically.

### The overlay uses the wrong theme

- Select **Follow Codex** and focus the Codex window once after changing its appearance.
- Choose **Light** or **Dark** for a fixed theme.
- If Codex is unavailable, Follow Codex falls back to the Windows application theme.

### The position is wrong after changing monitors

- Enable **Follow Codex between monitors**.
- Choose a left or right preset.
- Use **Reset** before saving a new custom position.

## Build from source

Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0), then run:

```powershell
git clone https://github.com/haroonsulahri/usage-overlay.git
cd usage-overlay
.\scripts\build.ps1
.\scripts\run.ps1
```

The solution has two main projects:

- `CodexUsage.Core`: parsing, calculations, settings, reset formatting, and log redaction.
- `UsageOverlay`: WPF interface, App Server lifecycle, window tracking, shortcuts, and Windows integration.

## Demo mode

Demo mode renders the UI without connecting to Codex:

```powershell
dotnet run --project .\src\UsageOverlay\UsageOverlay.csproj `
    --configuration Release -- --demo=75 --expanded
```

Useful states:

| Value | Expected result |
| --- | --- |
| `--demo=25` | Green rail, 75% left |
| `--demo=75` | Amber rail, 25% left |
| `--demo=92` | Red rail, 8% left |

Open the Settings window directly:

```powershell
dotnet run --project .\src\UsageOverlay\UsageOverlay.csproj `
    --configuration Release -- --demo=75 --settings
```

Other launch options:

| Option | Purpose |
| --- | --- |
| `--expanded` | Opens the detail card on launch |
| `--start-hidden` | Starts with the usage rail manually hidden |
| `--settings` | Opens Settings on launch |

## Tests

Run the local quality gate:

```powershell
.\scripts\build.ps1
```

This restores and builds the Release configuration with warnings treated as errors, verifies the multi-size Windows icon, and runs the dependency-free specification suite.

Run the authenticated smoke test only on a machine already signed in to Codex:

```powershell
.\scripts\smoke-live.ps1
```

The smoke test starts App Server, waits for a real rate-limit snapshot, reports the remaining and used percentages, and closes its child process.

The complete manual acceptance matrix is in [Testing](docs/testing.md).

## Create a release

Build the self-contained Windows x64 package:

```powershell
.\scripts\package-release.ps1
.\scripts\validate-release.ps1
```

Release files are written under `artifacts\release` and excluded from Git. The zip contains the executable, licence, documentation, icon assets, and shortcut helpers. The release directory also contains the manifest and SHA-256 checksum.

Pushing a semantic version tag such as `v0.1.0` runs the GitHub release workflow and uploads the generated Windows package.

## Uninstall

1. Quit Usage Overlay from the rail or tray menu.
2. Run the two `uninstall-*.ps1` shortcut scripts if you installed the shortcuts.
3. Delete the extracted application directory.
4. Optionally delete `%LOCALAPPDATA%\UsageOverlay` to remove your saved settings and logs.

Usage Overlay does not install a Windows service, browser extension, driver, or background updater.

## Limitations

- Windows x64 only for this preview.
- Designed for the Microsoft Store Codex desktop package.
- Requires a separately installed and authenticated Codex CLI.
- Usage is near-real-time, not token-by-token.
- The executable is not code-signed yet, so SmartScreen may warn on first launch.
- Follow Codex infers light or dark from the rendered window because the desktop app does not expose a public theme integration for companion tools.
- Usage Overlay is a separate companion window because Codex does not provide a public permanent desktop-chrome extension point.

## Contributing

Focused bug reports and pull requests are welcome. Please read [Contributing](CONTRIBUTING.md), the [Code of Conduct](CODE_OF_CONDUCT.md), and [Support](SUPPORT.md) before opening an issue.

Do not include credentials, account identifiers, private conversations, unreviewed logs, or private screenshots in public issues.

## Licence

Usage Overlay is released under the [MIT Licence](LICENSE).

Built by [Haroone.com](https://haroone.com). Codex and OpenAI are referenced only to describe compatibility.
