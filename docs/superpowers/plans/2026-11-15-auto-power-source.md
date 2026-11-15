# Auto Refresh Rate by Power Source – Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Plugged in → higher of Rate A/B, on battery → lower, applied only on real power-source changes and at start.

**Architecture:** Pure decision code (`RateLogic.RateForPowerSource`, `PowerSourceTracker`) is unit-tested; `TrayApp` wires `SystemEvents.PowerModeChanged` to it and shares one `SwitchTo` with the manual toggle.

**Tech Stack:** .NET 10, WinForms, xUnit.

**Spec:** `docs/superpowers/specs/2026-11-15-auto-power-source-design.md`

## Global Constraints

- Config field name `AutoByPowerSource`, default `true`; files without it load as `true`.
- Notifications: `🔌 {from} → {to} Hz` when plugged in, `🔋 {from} → {to} Hz` on battery; silent when already at target.
- Menu text: `Auto: {higher} Hz plugged in, {lower} Hz on battery`; hidden when the machine has no battery.
- A manual toggle is never undone until the next real AC ↔ battery transition.

## Review Focus

1. Battery-percentage ticks raise `StatusChange` with unchanged AC state → must not switch (tracker test `Same_state_again_is_not_a_change`).
2. `PowerLineStatus.Unknown` (passed as `null`) → no switch, and the next known state counts as a change only if it differs from the last *known* one (`Unknown_is_ignored_and_does_not_reset`).
3. Unplugged during sleep → `Resume` event with a new state → switches (same tracker path as StatusChange).
4. Auto turned off → tracker still updates, so re-enabling does not replay an old transition; enabling applies the current rule once (author-facing call, noted in summary).
5. Target rate unsupported by a monitor → existing skip/failed messages, prefixed with the power icon.

---

### Task 1: Pure logic and config field

**Files:**
- Modify: `src/RefreshToggle/RateLogic.cs`, `src/RefreshToggle/Config.cs`
- Test: `tests/RefreshToggle.Tests/RateLogicTests.cs`, `tests/RefreshToggle.Tests/ConfigTests.cs`

**Interfaces:**
- Produces: `int RateLogic.RateForPowerSource(bool pluggedIn, int rateA, int rateB)`;
  `sealed class PowerSourceTracker { bool? Observe(bool? pluggedIn); }` – returns the new state on the first known observation or a real change, else `null`;
  `Config.AutoByPowerSource` (bool, default `true`, last positional parameter).

- [ ] **Step 1: Failing tests**
  - `RateForPowerSource_picks_higher_when_plugged_in`: `(true, 60, 120) → 120`, `(true, 120, 60) → 120`, `(false, 60, 120) → 60`, `(false, 144, 60) → 60`.
  - `First_known_state_is_reported`: `Observe(true) == true`.
  - `Same_state_again_is_not_a_change`: `Observe(true); Observe(true) == null`.
  - `Real_change_is_reported`: `Observe(true); Observe(false) == false`.
  - `Unknown_is_ignored_and_does_not_reset`: `Observe(true); Observe(null) == null; Observe(true) == null; Observe(false) == false`.
  - Config: `Old_file_without_auto_field_enables_auto` (v0.4.0 JSON → `AutoByPowerSource == true`); extend round-trip test with `AutoByPowerSource: false`.
- [ ] **Step 2:** `dotnet test tests/RefreshToggle.Tests` → compile errors for the missing members.
- [ ] **Step 3:** Implement the three members above (`PowerSourceTracker` lives in `RateLogic.cs`).
- [ ] **Step 4:** `dotnet test tests/RefreshToggle.Tests -c Release` → all pass.
- [ ] **Step 5:** Commit `feat: power-source rate logic and config flag`.

### Task 2: Wire it into the tray app

**Files:**
- Modify: `src/RefreshToggle/TrayApp.cs`, `README.md`, `CHANGELOG.md`

**Interfaces:**
- Consumes: Task 1 members.

- [ ] **Step 1:** Extract `private void SwitchTo(int target, string prefix)` from `Toggle()` (refresh, skip unsupported, collect failures, notify with `prefix` before `{from} → {to} Hz`, silent if every monitor is already at `target`). `Toggle()` becomes `SwitchTo(RateLogic.NextRate(...), "")`.
- [ ] **Step 2:** Add `_power = new PowerSourceTracker()`, `bool HasBattery` (`!SystemInformation.PowerStatus.BatteryChargeStatus.HasFlag(BatteryChargeStatus.NoSystemBattery)`), `bool? ReadPluggedIn()` (`Online → true`, `Offline → false`, else `null`), and `ApplyPowerRule(bool pluggedIn)` → `SwitchTo(RateForPowerSource(...), pluggedIn ? "🔌 " : "🔋 ")`.
- [ ] **Step 3:** Subscribe `SystemEvents.PowerModeChanged` (StatusChange, Resume) → `Observe(ReadPluggedIn())`; if non-null and `_config.AutoByPowerSource && HasBattery` → `ApplyPowerRule`. Same call once at the end of the constructor. Unsubscribe in `Dispose`.
- [ ] **Step 4:** Menu: checkbox per Global Constraints after "Start with Windows", only when `HasBattery`; click saves the flipped flag and, when turning on, applies the rule for the current known state.
- [ ] **Step 5:** README (Use section + config example) and CHANGELOG 0.5.0 entry.
- [ ] **Step 6:** `dotnet build RefreshToggle.slnx -c Release` clean; `dotnet test RefreshToggle.slnx -c Release` green.
- [ ] **Step 7:** Commit `feat: switch refresh rate automatically by power source` and push.
