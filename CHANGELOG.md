# Changelog

All notable changes to RefreshToggle are documented here.

## [0.4.0] - 2026-07-19

### Added

- Layered Core, Application, Windows infrastructure, WinForms UI, and Tests projects on pinned .NET SDK 10.0.302.
- Immutable application state, serialized commands, typed infrastructure results, and deterministic lifecycle ownership.
- Transactional multi-display preflight, apply, read-back verification, and best-effort rollback.
- Strict hotkey validation and transactional registration changes that keep the old hotkey active until persistence succeeds.
- Versioned configuration migration, corrupt-file backup, serialized atomic writes, and recovery warnings.
- Verified distribution manifest, atomic self-install, nonce-bound helper/reaper uninstall, and stale cleanup hardening.
- Read-only `--health-check`, x64 and ARM64 release artifacts, SHA-256 checksums, and GitHub Artifact Attestations.
- Locked dependency restore, NuGet vulnerability audit, warnings-as-errors builds, coverage enforcement, and SHA-pinned GitHub Actions.

### Changed

- All active displays now toggle together only when the target rate is supported everywhere; partial toggles are disabled.
- Display events now use a true trailing 500 ms debounce.
- The tray icon renders complete refresh-rate labels, including three-digit rates, with an embedded fallback.
- Startup state is sourced exclusively from the current-user Registry Run entry; legacy JSON startup state is migrated away.
- Official releases target self-contained single-file `win-x64` and `win-arm64` on .NET 10.

### Fixed

- Installation replacement no longer risks corrupting a previously installed executable on copy or hash failure.
- Uninstall no longer executes cleanup from the mapped installed binary or accepts target paths from command-line arguments.
- Config directory, read, backup, and save failures no longer prevent startup where safe defaults can be used.
- Concurrent click, hotkey, and display-event commands can no longer mutate display or configuration state simultaneously.
- Shutdown now removes events, timers, native hotkeys, handles, icons, and cancellation resources deterministically on the UI thread where required.
- Double-click no longer causes an extra toggle; only a left mouse click toggles.

### Security and compatibility notes

- Release binaries are not Authenticode-signed. Verify `SHA256SUMS.txt` and the GitHub Artifact Attestation.
- Configuration under `%APPDATA%\RefreshToggle` is intentionally retained during uninstall.
- Per-monitor profiles and partial display changes are not supported.

## [0.3.0]

- Added multi-monitor support and self-install/uninstall behavior.

## [0.2.0]

- Added dynamic tray icons and x64/ARM64 single-file releases.

## [0.1.0]

- Initial tray utility release.
