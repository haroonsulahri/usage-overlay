# Testing

## Automated checks

Run the local quality gate:

```powershell
.\scripts\build.ps1
```

It performs:

- Release restore and build
- Compiler and analyzer checks with warnings treated as errors
- Dependency-free specifications for parsing, calculations, settings, and log redaction

## Authenticated smoke test

Run this only on a Windows machine already signed in to Codex:

```powershell
.\scripts\smoke-live.ps1
```

The smoke test starts App Server, waits up to 20 seconds for a real rate-limit snapshot, prints every returned usage window, then shuts down its child process.

## UI preview

Demo mode does not start App Server:

```powershell
dotnet run --project .\src\UsageOverlay\UsageOverlay.csproj `
    --configuration Release -- --demo=75 --expanded
```

Recommended visual states:

| Demo value | Expected state |
| --- | --- |
| `25` | Green rail, 75% left |
| `75` | Amber rail, 25% left, with the weekly detail row |
| `92` | Red rail, 8% left, with the weekly detail row |

## Manual acceptance matrix

| Scenario | Expected behavior |
| --- | --- |
| Connecting | Empty neutral rail and non-blocking status |
| Codex active | Rail appears at the saved or preset location |
| Codex inactive or minimized | Overlay hides |
| Codex file picker or Open/Save dialog | Overlay hides instead of attaching to the dialog |
| Hover | Detail card opens without taking keyboard focus |
| Multiple usage windows | The main Codex primary and secondary windows appear as separate labelled rows; model-specific buckets stay hidden |
| View usage link | A compact link remains visible at the bottom of the detail card and opens the Codex usage page |
| Click | Detail card pins or unpins |
| Close button | Pinned detail card collapses; rail and process remain active |
| Escape | Pinned detail card collapses; Settings window closes when it is active |
| Drag | Custom relative position is saved and restored |
| Manual hide | Usage continues updating; tray shows Show overlay |
| Relaunch while hidden | Existing instance becomes eligible to show; no duplicate remains |
| Windows Search launch | Existing or new instance opens Settings; no duplicate remains |
| Automatic startup | Overlay starts without opening Settings |
| Settings title bar | Minus hides Settings, Close closes Settings, and maximize is unavailable |
| Visibility controls | Codex-only and fullscreen choices appear together near the top |
| Visibility scope | Only in Codex and Across Windows map to the saved visibility setting |
| Connection details | Technical fields are collapsed by default and can be expanded |
| Settings initial scroll | General and Visibility are shown when the window opens |
| Pause | Overlay hides for 15 minutes, then resumes automatically |
| Fullscreen exclusion | Overlay hides only while Codex is fullscreen |
| Reduced motion | Values update without animated interpolation |
| Follow Codex theme | Overlay matches the active Codex light/dark surface and uses Windows as fallback |
| Dark theme | Settings, rail, detail card, and menus use the dark palette |
| Light theme | Settings, rail, detail card, and menus use the light palette with readable contrast |
| App Server exit | Overlay reconnects after a bounded delay |
| Disconnect Codex CLI | App Server child process exits, usage becomes unavailable, and the menu changes to Reconnect Codex CLI |
| Reconnect Codex CLI | A fresh App Server child process starts and live usage returns |
| Threshold notification | An enabled alert appears once when a returned window first crosses amber or red |
| Manual update check | Latest GitHub release metadata is checked only after user action; the result is shown without installing anything |
| Settings Save | Validated values persist to JSON and apply immediately when safe |
| Save hover | Primary action stays green and shows a clear light border |
| Settings Cancel | Unsaved changes are discarded |
| Second launch | Existing process receives the show signal; no duplicate remains |

## Release verification

Before tagging a release:

1. Run `scripts\build.ps1`.
2. Run `scripts\smoke-live.ps1` on a supported authenticated account.
3. Inspect normal, warning, and critical demo states.
4. Test Start-menu and Startup shortcut install/uninstall scripts.
5. Install Inno Setup 6 or set `INNO_SETUP_COMPILER` to `ISCC.exe`.
6. Run `scripts\package-release.ps1` and `scripts\validate-release.ps1`.
7. Run `scripts\test-installer.ps1` to verify Installed apps registration, launch, and uninstall cleanup.
8. Extract the portable zip into a clean directory and run the executable.
9. Verify both SHA-256 values in `SHA256SUMS.txt`.
