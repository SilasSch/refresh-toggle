# RefreshToggle

RefreshToggle is a small Windows tray application that switches all active displays between two configured refresh-rate profiles. It validates the complete multi-monitor change before applying anything, so unsupported targets never leave displays in a partially changed state.

## Download

Download the matching executable from [GitHub Releases](../../releases):

| Asset | Windows device |
| --- | --- |
| `RefreshToggle-win-x64.exe` | Intel and AMD 64-bit PCs |
| `RefreshToggle-win-arm64.exe` | ARM64 PCs, including Snapdragon devices |

Official releases are self-contained .NET 10 single-file applications. No separate .NET runtime is required. They are not Authenticode-signed, so Windows may show a SmartScreen warning. Verify the checksum and GitHub attestation before running a downloaded binary.

```powershell
Get-FileHash .\RefreshToggle-win-x64.exe -Algorithm SHA256
gh attestation verify .\RefreshToggle-win-x64.exe --repo SilasSch/refresh-toggle
```

Compare the first result with `SHA256SUMS.txt` attached to the same release.

## Behavior

- Left-clicking the tray icon or pressing the configured global hotkey toggles all active displays.
- Rate A and Rate B are global profiles. Before every toggle, RefreshToggle enumerates displays again and enables a target only if every active display supports it.
- Every mode is preflight-tested, then applied, read back, and verified. If one display fails, already changed displays are restored best-effort.
- The tray icon renders the full current rate, including three-digit values such as 100, 105, and 200 Hz. An embedded static icon is used if dynamic rendering fails.
- Display-change events use a trailing 500 ms debounce, so event storms settle before one final refresh.

The rate picker shows the union of detected refresh rates and labels each option as supported by N of M displays. A rate that is not supported by all active displays can be saved as a profile but cannot be toggled until the display set supports it.

## Configuration

Configuration is stored at `%APPDATA%\RefreshToggle\config.json`:

```json
{
  "SchemaVersion": 1,
  "RateA": 60,
  "RateB": 120,
  "HotkeyModifiers": "Ctrl+Alt",
  "HotkeyKey": "T"
}
```

Unversioned configuration files are migrated automatically. The legacy `StartWithWindows` field is ignored and removed on the next successful save; the current-user Registry Run entry is the only startup source of truth. Damaged JSON is preserved best-effort as `config.corrupt-<UTC>.json`, after which safe defaults are used.

## Installation and uninstall

Only official distribution binaries may install themselves into `%LOCALAPPDATA%\RefreshToggle` or change startup registration. Debug builds, framework-dependent builds, and ordinary source publishes run in portable development mode; their Startup and Uninstall actions are disabled with an explanation.

Official builds install through a verified temporary copy and atomic replacement. Uninstall hands cleanup to nonce-bound temporary helper processes, removes the installed executable and startup entry, and keeps `%APPDATA%\RefreshToggle\config.json` deliberately. A locked executable is retried and can be scheduled for deletion at the next reboot.

## Build from source

The repository pins SDK `10.0.302` in `global.json`.

```powershell
dotnet restore RefreshToggle.sln --locked-mode
dotnet build RefreshToggle.sln -c Release --no-restore
dotnet test tests/RefreshToggle.Tests/RefreshToggle.Tests.csproj -c Release --no-build --no-restore
```

A normal source publish remains portable by design. To reproduce the release layout for inspection:

```powershell
dotnet publish src/RefreshToggle.App/RefreshToggle.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:DistributionBuild=true -o artifacts/win-x64
```

`DistributionBuild=true` is intended for the pinned GitHub workflow. The runtime additionally verifies that the process is an actual self-contained single-file Windows bundle with a matching RID before permitting installation changes.

## Quality and release process

Pull requests and `master` run locked restore with NuGet audit, formatting verification, warnings-as-errors Release builds, 80% minimum line coverage for Core and Application, tests, both Windows publishes, PE architecture checks, and the x64 read-only `--health-check`. Release assets include SHA-256 checksums and GitHub Artifact Attestations.

See [CHANGELOG.md](CHANGELOG.md) for release history.

## License

[MIT](LICENSE)
