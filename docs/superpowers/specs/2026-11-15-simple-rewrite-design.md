# RefreshToggle – Simple Rewrite (v0.5.0)

## Why

The only user is the author. v0.4.0 grew to ~5,800 lines across five projects; 29 % of it is
self-install/uninstall machinery (helper + reaper processes, nonces, named events) for a single
personal exe. A rewrite should make the code small enough to read in one sitting.

## Goals

- Same everyday behaviour: left-click or global hotkey toggles all active monitors between Rate A
  and Rate B; tray icon shows the current Hz; Rate A/B and hotkey changeable from the menu;
  "Start with Windows" checkbox.
- Existing `%APPDATA%\RefreshToggle\config.json` keeps working (same field names).
- ~500 lines of app code in one project; small test project for pure logic.
- CI builds and tests on every push/PR; tags `v*` publish x64 + ARM64 single-file exes.

## Non-goals / dropped

- Self-install to `%LOCALAPPDATA%`, uninstall, cleanup/reaper helpers, deployment manifest.
- Transactional preflight/apply/read-back/rollback, command gate, ports/adapters layering.
- Artifact attestations, SHA256SUMS, `--health-check`, coverage gate, NuGet lock files,
  central package management, rolling `latest` prerelease.
- Per-app switching (issue #2) – possible later, not part of this rewrite.

## Anti-goals

- No new abstraction layers "for testability". Pure logic lives in static helpers that tests link
  directly; Win32 code stays thin and untested.

## Approaches considered

- **A – rewrite as one C# WinForms project (chosen).** Smallest result, stays on the author's stack.
- B – strip down the existing code. More work, still ends at 1,500–2,000 lines with nested layers.
- C – AutoHotkey/PowerShell script. Tiny, but loses the rendered Hz icon and a solid tray UX.

## Design

```
src/RefreshToggle/        net10.0-windows, WinForms, single exe
  Program.cs              single-instance mutex, Application.Run(new TrayApp())
  TrayApp.cs              NotifyIcon, context menu, click → toggle, debounced display events
  Displays.cs             EnumDisplayDevices / EnumDisplaySettings / ChangeDisplaySettingsEx
  RateLogic.cs            pure: next target rate, common rates across monitors
  Hotkey.cs               pure parse/format + RegisterHotKey via NativeWindow
  HotkeyDialog.cs         small "press the new combination" form
  Config.cs               pure: load/save JSON, invalid → defaults
  Startup.cs              HKCU\...\Run "RefreshToggle" = "<this exe>"
  IconRenderer.cs         rounded square with the Hz number (A = blue, B = green, other = grey)
tests/RefreshToggle.Tests net10.0 xUnit, links RateLogic.cs, Config.cs, Hotkey parsing
```

Behaviour decisions (author's calls, correctable):

- **Toggle:** target = the other rate relative to the primary monitor's current rate (closest
  rate rule kept). Each active monitor that supports the target at its current resolution is
  switched; monitors that don't are skipped and named in the balloon. No read-back/rollback.
- **Feedback:** balloon "60 → 120 Hz" after every toggle (covers issue #4); every error shows a
  balloon, also when it repeats.
- **Rate menu:** "Rate A ▸" / "Rate B ▸" submenus list rates supported by *all* active monitors,
  current choice checked. No dialog.
- **Hotkey:** default `Ctrl+Shift+R` (unchanged), `MOD_NOREPEAT`. New hotkey is registered first;
  only on success is the old one released and the config saved.
- **Display events:** `SystemEvents.DisplaySettingsChanged` restarts a 500 ms WinForms timer;
  everything runs on the UI thread – no locks, no async.
- **Config:** same fields (`RateA`, `RateB`, `HotkeyModifiers`, `HotkeyKey`, `SchemaVersion: 1`);
  unknown/legacy fields ignored; unreadable or invalid file → defaults (balloon), rewritten on next
  save. Save = write temp file + `File.Move(overwrite)`.
- **Startup:** checkbox is checked when the Run value points at the running exe; toggling writes or
  deletes the value.
- **Migration from v0.4.0:** the old installed copy in `%LOCALAPPDATA%\RefreshToggle` is left alone;
  README explains how to remove it. Enabling "Start with Windows" in the new exe overwrites the
  Run entry.
- **Build:** no `global.json` pin (CI uses `10.0.x`); version 0.5.0 in the csproj; Release builds
  treat warnings as errors.

## Left to the builder

Menu wording, exact icon font sizing, test case selection.
