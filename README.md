# RefreshToggle

**Lightweight Windows system tray utility to toggle display refresh rates between two configured values.**

Download the latest release and run — no .NET installation required.

## Features

- **One-click toggle** — left-click the tray icon to switch refresh rates instantly
- **Dynamic tray icon** — shows your current Hz with color coding (green = high, gray = low)
- **Multi-monitor support** — per-display toggle entries in the context menu
- **Auto-start** — optional "Start with Windows" via context menu
- **Self-installing** — copies itself to `%LOCALAPPDATA%\RefreshToggle\` for clean auto-start
- **Portable** — single self-contained `.exe`, no runtime needed
- **ARM64 compatible** — .NET 8, runs on Snapdragon / ARM laptops

## Download

Grab the latest release from [GitHub Releases](../../releases):

| File | Platform |
|------|----------|
| `RefreshToggle-x64.exe` | 64-bit PCs (Intel / AMD) |
| `RefreshToggle-arm64.exe` | ARM64 (Snapdragon laptops) |

Just run the `.exe` — it's fully self-contained.

## Configuration

On first launch, a config file is created at `%APPDATA%\RefreshToggle\config.json`:

```json
{
  "RateA": 60,
  "RateB": 120,
  "StartWithWindows": false
}
```

You can configure `RateA` and `RateB` directly from the tray menu using **Set Rate A...** / **Set Rate B...**.
The app lists supported rates for your primary display and saves changes to `config.json` automatically.

> `StartWithWindows` is managed automatically by the tray menu — no need to edit it manually.

## Tray Menu

| Action | Description |
|--------|-------------|
| **Left-click icon** | Toggle refresh rate |
| **Per-display entries** | Toggle rate for a specific monitor |
| **Set Rate A... / Set Rate B...** | Pick configured target rates from supported display modes |
| **Start with Windows** | Register / unregister auto-start |
| **Uninstall** | Remove installed copy and startup entry |
| **Exit** | Close the app |

## Build from Source

```bash
# Restore & build
dotnet build

# Run
dotnet run

# Publish portable single-file
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o ./publish/win-x64
dotnet publish -c Release -r win-arm64 --self-contained -p:PublishSingleFile=true -o ./publish/win-arm64
```

Releases are built automatically via GitHub Actions on every `v*` tag push.

## Changelog

### v0.3.0
- Multi-monitor support — per-display refresh rate toggle in tray menu (#14)
- Auto-install to `%LOCALAPPDATA%` — install/uninstall from tray (#13)

### v0.2.0
- Dynamic tray icon with Hz display and color coding (#9)
- Portable single-file `.exe` releases for x64 and ARM64 (#10)

### v0.1.0
- Initial release — single-display toggle, config file, system tray

## License

MIT
