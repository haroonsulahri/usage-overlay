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

The smoke test starts App Server, waits up to 20 seconds for a real rate-limit snapshot, prints the remaining and used percentages, then shuts down its child process.

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
| `75` | Amber rail, 25% left |
| `92` | Red rail, 8% left |

## Manual acceptance matrix

| Scenario | Expected behavior |
| --- | --- |
| Connecting | Empty neutral rail and non-blocking status |
| Codex active | Rail appears at the saved or preset location |
| Codex inactive or minimized | Overlay hides |
| Hover | Detail card opens without taking keyboard focus |
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
| System theme | Overlay follows Windows app-theme changes without restart |
| Dark theme | Settings, rail, detail card, and menus use the dark palette |
| Light theme | Settings, rail, detail card, and menus use the light palette with readable contrast |
| App Server exit | Overlay reconnects after a bounded delay |
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
5. Run `scripts\package-release.ps1`.
6. Extract the zip into a clean directory and run the executable.
7. Verify the SHA-256 value in `SHA256SUMS.txt`.
