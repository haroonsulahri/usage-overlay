# Verification contract

## Target

A Windows companion overlay for the packaged Codex desktop application.

## Baseline

- Codex desktop is installed as the `OpenAI.Codex` package and its top-level process is `ChatGPT.exe`.
- The authenticated Codex CLI exposes App Server usage data through `account/rateLimits/read`.
- No supported extension point places a permanent plugin component in the desktop app chrome.

## Expected outcome

- A narrow vertical usage rail with a compact remaining-percentage label appears only while the Codex desktop window is active.
- The rail and primary label reflect how much Codex quota remains.
- Used percentage remains available as secondary context.
- Usage changes animate smoothly from the previous percentage.
- Hovering expands a compact detail card; clicking pins or unpins it.
- The overlay does not modify, inject into, or restart Codex.
- The overlay does not read or store account credentials.
- Custom drag placement and persisted display controls remain available from the rail and tray.
- Automatic startup keeps the companion waiting silently until Codex becomes active.

## State matrix

| State | Rail | Detail panel | Colour | Expected behavior |
| --- | --- | --- | --- | --- |
| Connecting | Empty | Connection status | Neutral | Retry without blocking Codex |
| 0% to 69% used | Remaining fill | Remaining, used, and reset | Green | Normal status |
| 70% to 89% used | Remaining fill | Remaining, used, and reset | Amber | Calm warning |
| 90% to 100% used | Remaining fill | Remaining, used, and reset | Red | Near-limit warning |
| Codex inactive | Hidden | Hidden | N/A | Never float over other applications |
| Manually hidden | Hidden | Hidden | N/A | Continue usage updates and expose Show overlay in the tray |
| Relaunched while hidden | Restored when Codex is active | Collapsed | Current threshold | Signal the existing process instead of starting a duplicate |
| Rail dragged | Saved custom location | Normal | Current threshold | Restore the relative location after resizing or restarting |
| Paused | Hidden | Hidden | N/A | Resume automatically after 15 minutes |
| Fullscreen exclusion enabled | Hidden in fullscreen | Hidden | N/A | Resume outside fullscreen |
| Reduced motion | Final value | Normal | Threshold colour | No looping or decorative motion |

## Must remain unchanged

- Codex application files, DOM, state database, configuration, conversations, and authentication files.
- Other `ChatGPT.exe` packages, including ChatGPT Classic.
- Windows startup configuration unless the user enables it separately.

## Verification evidence

- Release build succeeds with warnings treated as errors.
- Parser and formatting specs pass.
- Live App Server smoke test returns a real quota snapshot.
- The compiled overlay anchors to the current Codex window and hides when Codex is not active.
- Visual states are checked at normal, warning, and critical percentages.
