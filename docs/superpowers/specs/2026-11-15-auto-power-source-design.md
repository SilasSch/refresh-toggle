# Auto refresh rate by power source

## Why

The author toggles mainly for battery life. The laptop starts at 60 Hz, so they often forget to
switch to 120 Hz when plugged in. Windows has no built-in rule for this: Dynamic Refresh Rate
reacts to on-screen activity, not to the power source, and needs a VRR panel.

## Goals

- Plugged in → the higher of Rate A/B; on battery → the lower one.
- Applied when the power source actually changes (plug/unplug, also detected after resume from
  sleep) and once at app start.
- A manual toggle always wins until the next real power-source change. Example: on battery,
  manually 120 Hz → stays 120 Hz until plugged in or unplugged again.
- On by default; a menu checkbox turns it off. Persisted in `config.json`.

## Non-goals

- No per-app rules (issue #2), no schedules, no battery-percentage thresholds.
- No change on machines without a battery (desktops): the menu item is hidden.

## Approaches

- **A – `SystemEvents.PowerModeChanged` + compare `PowerLineStatus` with the last known value
  (chosen).** Windows raises status changes for every battery-percentage tick, so only an actual
  AC ↔ battery transition may act; that comparison is what keeps manual overrides intact.
- B – `RegisterPowerSettingNotification(GUID_ACDC_POWER_SOURCE)`: only AC/DC events, but ~40
  lines of extra Win32 for no practical gain.
- C – Task Scheduler trigger on power events: external setup, and the app already runs.

## Design

- `Config`: new field `AutoByPowerSource` (bool, default `true`; older files without it → `true`).
- `RateLogic.RateForPowerSource(bool pluggedIn, int rateA, int rateB)` → max or min. Unit-tested.
- `TrayApp`:
  - Remember `_lastPowerLine`. On `PowerModeChanged` (StatusChange or Resume) read
    `SystemInformation.PowerStatus.PowerLineStatus`; `Unknown` is ignored; only a change from the
    remembered value switches.
  - At start, if enabled and the state is known, switch to the matching rate.
  - Toggle and auto share one `SwitchTo(target, prefix)` with the existing skip/failed handling.
    Notification `🔌 60 → 120 Hz` / `🔋 120 → 60 Hz`; silent when already at the target.
  - Menu checkbox `Auto: 120 Hz plugged in, 60 Hz on battery` (numbers from the config), hidden
    when `BatteryChargeStatus` has `NoSystemBattery`.

## Left to the builder

Exact menu position and wording.
