RefreshToggle v0.4.0 is a stability-focused rewrite with transactional multi-monitor switching, durable configuration recovery, strict global hotkey changes, verified self-install/uninstall, deterministic shutdown, and a layered testable architecture on .NET 10.

Highlights:

- Every active display is preflight-tested before an all-or-none toggle; applied modes are read back and partial failures trigger best-effort rollback.
- Configuration writes are serialized and atomic, legacy files migrate to schema 1, and damaged files are retained best-effort before safe defaults load.
- Official x64 and ARM64 binaries are self-contained single files. Source and debug builds remain portable and cannot install themselves or change startup.
- Uninstall uses validated nonce-bound helper and reaper processes and deliberately retains the user configuration.
- Core and Application exceed the 80% line-coverage gate; CI also enforces locked restore, NuGet audit, formatting, warnings as errors, PE checks, and the read-only x64 health check.

There is no Authenticode signature for this release. Verify downloads with `SHA256SUMS.txt` and GitHub Artifact Attestations:

```powershell
Get-FileHash .\RefreshToggle-win-x64.exe -Algorithm SHA256
gh attestation verify .\RefreshToggle-win-x64.exe --repo SilasSch/refresh-toggle
```

See `CHANGELOG.md` for the complete change list and compatibility notes.
