# WinForge

WinForge is a local-first Windows inspection, cleanup, privacy, gaming, power, and recovery application. It uses typed operations, exact before-state journals, a separately launched worker, catalog validation, and verification after every supported change. It contains no telemetry, ads, account requirement, registry cleaner, timer myths, or arbitrary catalog PowerShell.

## Current capabilities

- Arabic RTL and English LTR WPF interface with dark/light themes.
- Real system, startup, service, scheduled task, Appx, optional-feature, power-plan, and network inspection.
- Two-stage cleaner with age thresholds, allowlisted roots, reparse-point blocking, locked-file handling, and exact results.
- JSON tweak/profile catalog with compatibility, dependency, conflict, risk, localization, and integrity validation.
- Privacy and Gaming catalog execution through an authenticated local named pipe and separate Worker; UAC is requested only for administrative operations.
- Exact registry absence/type/value rollback, service/task state capture, partial-batch rollback, structured local logs, and support bundles.
- Conservative power-plan parser and duplicate-plan prevention for `WinForge Balanced Gaming`.

## Supported systems

| OS | Status |
|---|---|
| Windows 11 x64 | Primary target |
| Windows 11 arm64 | Build target; hardware validation required |
| Windows 10 22H2 | Legacy compatibility; Microsoft consumer support has ended |

## Build

```powershell
./scripts/build.ps1
./scripts/test.ps1
./scripts/package.ps1 -Runtime win-x64
./scripts/package-installer.ps1
```

Run `src/WinForge.UI/bin/Debug/net8.0-windows/WinForge.UI.exe`. The UI is `asInvoker`; a separate Worker is launched only when applying a reviewed plan. Administrative plans show the standard Windows UAC prompt.

## Safety and rollback

Every execution plan is validated against the shipped catalog. Before the first mutation, WinForge writes a journal under `%ProgramData%\WinForge\Rollback\<SessionId>`. If a registry value did not originally exist, rollback deletes it instead of inventing a default. Cleaner actions are previews first and never follow reparse points.

See [tweak safety](docs/tweak-safety.md), [architecture](docs/architecture.md), and [VM testing](docs/VM_TESTING.md).

## Privacy

WinForge works without a network connection and sends no telemetry. Optional update checking is not enabled. Support bundles sanitize the user profile path and include only system facts, app version, and WinForge logs.

## Known limitations

- Appx removal and provisioned-package removal are modeled but not exposed as automatic bulk actions; protected and unknown packages stay read-only.
- Installer compilation requires Inno Setup on the release machine; portable packages do not require it.
- Code signing requires a user-supplied certificate in CI secrets or a local secure certificate store.
