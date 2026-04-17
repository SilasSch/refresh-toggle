# RefreshToggle

Lightweight Windows system tray utility that toggles the primary display refresh rate between two configured values.

## Features

- **One-click toggle** — left-click the tray icon to switch refresh rates
- **System tray** — tooltip shows current Hz, right-click for context menu
- **Configurable** — edit `%APPDATA%\RefreshToggle\config.json`
- **Single instance** — only one instance allowed
- **ARM64 compatible** — .NET 8, runs on Snapdragon laptops

## Build

```bash
dotnet build
```

## Run

```bash
dotnet run
```

## Download

Pre-built self-contained binaries (no .NET installation required) are available on the [Releases](../../releases) page:

- `RefreshToggle-x64.exe` — for standard 64-bit PCs
- `RefreshToggle-arm64.exe` — for Snapdragon / ARM64 laptops

## Publish (single .exe)

```bash
# x64
dotnet publish -c Release -r win-x64 --self-contained -o ./publish/win-x64

# ARM64
dotnet publish -c Release -r win-arm64 --self-contained -o ./publish/win-arm64
```

Releases are also built automatically via GitHub Actions when a `v*` tag is pushed.

## Config

On first run, `%APPDATA%\RefreshToggle\config.json` is created:

```json
{
  "RateA": 60,
  "RateB": 120
}
```

Edit the file to change your preferred refresh rates.
