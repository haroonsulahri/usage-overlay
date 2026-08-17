# Contributing

Thanks for helping improve Codex Usage Overlay. Small, focused changes are easiest to review and safest for a desktop companion that handles account metadata.

## Before opening an issue

- Check existing issues for the same problem.
- Confirm you are using the Microsoft Store Codex desktop app on Windows.
- Record the overlay version, Codex CLI version, Windows version, and authentication mode.
- Remove email addresses, local paths, account identifiers, and unrelated application content from screenshots and logs.

Use a security advisory instead of a public issue for suspected vulnerabilities. See [SECURITY.md](SECURITY.md).

## Development setup

Requirements:

- Windows 10 or Windows 11
- .NET 8 SDK
- Codex CLI for the optional live smoke test

Run the local quality gate:

```powershell
.\scripts\build.ps1
```

Preview the UI without account access:

```powershell
dotnet run --project .\src\CodexUsageOverlay\CodexUsageOverlay.csproj `
    --configuration Release -- --demo=75 --expanded
```

Run the authenticated smoke test only when you are already signed in to Codex:

```powershell
.\scripts\smoke-live.ps1
```

## Pull requests

- Keep the change scoped to one problem.
- Add or update tests for parsing, calculations, settings, or security-sensitive behavior.
- Preserve reduced-motion support, Codex-only visibility, single-instance behavior, and local-only data handling.
- Do not add telemetry, remote analytics, private API calls, credential access, or background uploads.
- Do not include generated binaries, logs, settings, screenshots with private content, or machine-specific paths.
- Update README, CHANGELOG, SECURITY, or architecture documentation when behavior changes.
- Run `scripts\build.ps1` before submitting.

## Coding conventions

- Treat compiler and analyzer warnings as errors.
- Prefer the .NET standard library and Windows platform APIs already in use.
- Keep App Server messages strongly bounded and treat returned data as untrusted input.
- Keep UI surfaces solid and GPU-safe. Do not add backdrop blur or large animated effects.
- Preserve the existing system typography and compact Codex-compatible visual language.

## Commit messages

Use a short imperative summary, for example:

```text
Handle missing reset timestamps
Keep custom position inside resized windows
Redact authorization headers in logs
```

