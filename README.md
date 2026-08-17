# Codex Usage Overlay

An unofficial Windows companion that places a slim live quota meter beside the Codex desktop chat. It uses the documented Codex App Server protocol and does not inject into or modify the Codex application.

## What it does

- Anchors a 10px vertical usage rail to the active Codex desktop window.
- Shows the remaining quota as the primary value.
- Drains smoothly from full to empty as the account quota is consumed.
- Animates changes over 620ms with a calm ease-out transition.
- Expands a detail card on hover and pins it on click.
- Shows the remaining percentage, used percentage, reset countdown, and one additional model bucket when available.
- Changes from green to amber at 70% and red at 90%.
- Hides whenever Codex is minimized or another application is active.
- Provides Refresh, Open log, and Exit actions from the system tray.

## Requirements

- Windows 10 or Windows 11
- .NET 8 Desktop Runtime
- Codex CLI available on `PATH`
- Codex signed in with ChatGPT-backed authentication

The account usage endpoints are not available with API-key-only or Bedrock authentication.

## Run from source

```powershell
cd C:\path\to\codex-usage-overlay
.\scripts\build.ps1
.\scripts\run.ps1
```

Keep the Codex desktop window active. The overlay appears near its lower-right edge.

## Preview a visual state

Demo mode does not start App Server and is useful for visual review:

```powershell
dotnet run --project .\src\CodexUsageOverlay\CodexUsageOverlay.csproj --configuration Release -- --demo=63
```

Try `--demo=25`, `--demo=75`, or `--demo=92` to inspect each colour state.
Add `--expanded` to open and pin the detail card for visual QA:

```powershell
dotnet run --project .\src\CodexUsageOverlay\CodexUsageOverlay.csproj --configuration Release -- --demo=75 --expanded
```

## Create a Windows package

```powershell
.\scripts\package.ps1
```

The framework-dependent single-file build is written to `artifacts\win-x64`.

## Verify live App Server access

Run the authenticated smoke test without opening the overlay UI:

```powershell
.\scripts\smoke-live.ps1
```

It exits successfully only after receiving a real Codex quota snapshot.

## Interaction

- Hover the rail to reveal quota details.
- Click the rail to pin or unpin the detail card.
- Right-click the rail for Refresh and Exit.
- Use the tray icon when the rail is hidden.

## Data and privacy

The overlay launches `codex app-server --stdio`, sends `account/rateLimits/read`, and listens for `account/rateLimits/updated`. Authentication remains owned by Codex. The overlay does not read, copy, transmit, or store credentials. Diagnostic logs contain status and error messages only and are stored at:

```text
%LOCALAPPDATA%\CodexUsageOverlay\overlay.log
```

## Limitations

- Usage is near-real-time and depends on when the Codex service updates the quota percentage.
- The first release targets the Microsoft Store Codex package on Windows.
- The overlay is a separate companion process because the current desktop app has no documented permanent chrome slot for plugins.
- Automatic Windows startup is intentionally not installed by default.

## License and disclaimer

MIT licensed. This is an unofficial community project and is not affiliated with or endorsed by OpenAI.
