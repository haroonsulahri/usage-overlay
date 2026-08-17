# Security policy

## Supported versions

This project is currently a preview. Security fixes are applied to the latest release and the default branch.

## Report a vulnerability

Do not open a public issue for a suspected vulnerability. Use the repository's private security advisory feature and include:

- A concise description of the issue
- Reproduction steps or a minimal proof of concept
- Affected version and Windows version
- Expected and observed behavior
- The security impact you believe is possible

Do not include real credentials, session tokens, private conversations, or another person's data.

## Trust boundaries

Usage Overlay:

- Starts the locally installed Codex CLI in App Server stdio mode
- Requests account rate-limit metadata only
- Uses Codex-owned authentication without reading authentication files
- Identifies the active Codex window through Windows process metadata
- Stores settings and redacted diagnostic logs under `%LOCALAPPDATA%\UsageOverlay`
- Creates Start-menu or Startup shortcuts only when the user requests them

It does not inject code into Codex, inspect conversation content, read cookies, expose a network listener, or send telemetry.

## Local attack considerations

The overlay executes the `codex` command found on `PATH`, unless `USAGE_OVERLAY_CODEX_PATH` points to a specific executable. The legacy `QUOTARAIL_CODEX_PATH` and `CODEX_USAGE_CODEX_PATH` variables remain supported for backward compatibility. Users should install Codex CLI from a trusted source and keep untrusted directories out of `PATH`.

Diagnostic messages are redacted and truncated before writing. Review logs before sharing them because error messages can still contain machine or environment details.

Release binaries should be downloaded from this repository's official Releases page and verified against the published SHA-256 checksum.
