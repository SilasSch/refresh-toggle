# RefreshToggle

A small Windows tray app that switches all monitors between two refresh rates (Rate A and Rate B)
with one click or a global hotkey.

## Use

- **Left-click** the tray icon or press the hotkey (default **Ctrl+Shift+R**) to toggle.
- The icon shows the current rate: **blue** = Rate A, **green** = Rate B, grey = anything else.
- After each toggle a notification shows the change, e.g. `60 → 120 Hz`. Monitors that don't
  support the target rate are skipped and named in the notification.
- **Auto by power source** (laptops, on by default): plugging in switches to the higher of
  Rate A/B, unplugging to the lower one (`🔌 60 → 120 Hz` / `🔋 120 → 60 Hz`). It also applies at
  start and after waking from sleep. A manual toggle stays until you plug in or unplug again, so
  120 Hz on battery is still one click away. Turn it off in the menu.
- **Right-click** for the menu: current rate per monitor, Rate A / Rate B (only rates every
  monitor supports are listed), hotkey, *Start with Windows*, *Auto*, Exit.

## Download

Grab `RefreshToggle-win-x64.exe` (Intel/AMD) or `RefreshToggle-win-arm64.exe` (Snapdragon/ARM)
from [Releases](../../releases). It's a single self-contained exe – put it anywhere and run it.
It isn't code-signed, so Windows SmartScreen may warn the first time.

## Settings

Stored in `%APPDATA%\RefreshToggle\config.json` (same format as v0.4.0):

```json
{
  "RateA": 60,
  "RateB": 120,
  "HotkeyModifiers": "Ctrl+Shift",
  "HotkeyKey": "R",
  "AutoByPowerSource": true,
  "SchemaVersion": 1
}
```

If the file is broken, defaults are used and a notification says so.
*Start with Windows* writes `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\RefreshToggle`
pointing at the exe you are running.

### Coming from v0.4.0

v0.4.0 installed itself to `%LOCALAPPDATA%\RefreshToggle`. v0.5.0 doesn't install anything.
Exit the old version from its tray menu, start the new exe, tick *Start with Windows* once, then
delete `%LOCALAPPDATA%\RefreshToggle` if you like. Your settings carry over.

## Build

Requires the .NET 10 SDK.

```powershell
dotnet test RefreshToggle.slnx
dotnet run --project src/RefreshToggle
```

The tests only cover the pure logic (`RateLogic`, `Config`, `HotkeyBinding`) and run on any OS.
CI builds, tests and publishes both exes on every push; pushing a `v*` tag creates a release.

## License

[MIT](LICENSE)
