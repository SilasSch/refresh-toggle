# RefreshToggle

Lightweight Windows system tray utility that toggles the primary display refresh rate between two configured values.

## Features

- **One-click toggle** — left-click the tray icon to switch refresh rates
- **System tray** — tooltip shows current Hz, right-click for context menu
- **Configurable** — edit `%APPDATA%\RefreshToggle\config.json`
- **Single instance** — only one instance allowed
- **ARM64 compatible** — .NET 8, runs on Snapdragon laptops
- **Start with Windows** — optional auto-start via context menu

## Build

```bash
dotnet build
```

## Run

```bash
dotnet run
```

## Publish (single .exe)

```bash
dotnet publish -c Release -r win-arm64 --self-contained -o ./publish
```

## Config

On first run, `%APPDATA%\RefreshToggle\config.json` is created:

```json
{
  "RateA": 60,
  "RateB": 120,
  "StartWithWindows": false
}
```

Edit the file to change your preferred refresh rates. The `StartWithWindows` field is managed automatically by the context menu item **Start with Windows** and reflects whether the app is registered to launch on login.
