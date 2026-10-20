# Testing

## Automated checks

Run the local quality gate:

```powershell
.\scripts\build.ps1
```

It performs:

- Release restore and build
- Compiler and analyzer checks with warnings treated as errors
- Dependency-free specifications for parsing, calculations, settings, and log redaction, including Claude status-line parsing, the Claude snapshot and host tracking, Cursor spend parsing, app identification and per-provider thresholds
- Claude Code integration checks against a temporary profile: connecting with and without an existing status line, idempotent reconnects, restoring the original, leaving a status line the user replaced, repairing the command after the executable moves, and refusing malformed settings
- Process-level checks using an isolated fake App Server and temporary `CODEX_HOME`: initial usage, external logout, login to a second account, direct account switching, late replies and child-process cancellation. The fake server also rejects any routine request that forces token refresh.

## Authenticated smoke test

Run this only on a Windows machine already signed in to Codex:

```powershell
.\scripts\smoke-live.ps1
```

The smoke test starts App Server, reads a real rate-limit snapshot, verifies disconnect/reconnect and checks the public release endpoint within a 30-second deadline.

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

Add `--provider=claude` or `--provider=cursor` to lead with that app; the others appear under **Other apps**.

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
| Custom-position hover | The rail stays fixed while the detail card expands upward and left |
| Manual hide | Usage continues updating; tray shows Show overlay |
| Relaunch while hidden | Existing instance becomes eligible to show; no duplicate remains |
| Windows Search launch | Existing or new instance opens Settings; no duplicate remains |
| Automatic startup | Overlay starts without opening Settings |
| Settings title bar | Minus hides Settings, Close closes Settings, and maximize is unavailable |
| Visibility controls | Codex-only and fullscreen choices appear together near the top |
| Visibility scope | Only in these apps and Across Windows map to the saved visibility setting |
| Connection details | Technical fields are collapsed by default and can be expanded |
| Settings initial scroll | General and Visibility are shown when the window opens |
| Pause | Overlay hides for 15 minutes, then resumes automatically |
| Fullscreen exclusion | Overlay hides only while Codex is fullscreen |
| Reduced motion | Values update without animated interpolation |
| Follow app theme | Overlay matches the active app's light/dark surface and uses Windows as fallback |
| Dark theme | Settings, rail, detail card, and menus use the dark palette |
| Light theme | Settings, rail, detail card, and menus use the light palette with readable contrast |
| App Server exit | Overlay reconnects after a bounded delay |
| Logout saved by Codex | Old usage clears when login-file metadata changes; the overlay reconnects automatically |
| Login or direct account switch saved by Codex | New account usage loads without restarting the overlay; previous-account replies are rejected |
| Credential manager without an observable login file | Overlay-owned session is renewed every minute to reload credentials |
| Revoked refresh token | Usage is unavailable until Codex is signed in again; saving the new login triggers reconnection |
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
| Claude on, no status line | Claude Code shows `5h N% left · 7d N% left`; rail shows Claude beside the Claude desktop app |
| Claude on, existing status line | The user's status line output is unchanged; Claude usage still updates |
| Claude off | `~/.claude/settings.json` returns to its previous `statusLine` |
| Claude Code in Windows Terminal or VS Code | After a reply, focusing that window shows the Claude rail; File Explorer and the desktop do not |
| Claude Code exits | Its terminal stops showing the Claude rail within a few seconds |
| Claude reading older than 15 minutes | Status shows its age in amber; past resets show the window as reset |
| Cursor with valid key and email | Rail shows spend against the limit; card shows the amounts |
| Cursor with rejected key or unknown email | Card shows `Key rejected` or `Not found` without retry storms |
| Other apps | Card lists enabled apps; clicking one shows it until another app comes to the front |
| Tray tooltip | Summarises each enabled app with usage |
| Uninstall with Claude on | The previous Claude Code status line is restored |
