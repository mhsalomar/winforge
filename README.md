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

- Startup, Services, Scheduled Tasks, Appx, Optional Features, Network, and Repair pages currently provide real inspection and exact-operation infrastructure, but their per-row mutation controls are not exposed in the 1.0 UI. Privacy, Gaming, Cleaner, support bundles, and session rollback are the implemented write flows.
- Appx/provisioned-package removal, profile import/export, single-operation rollback, restore-point creation, and benchmark A/B orchestration are not exposed in 1.0.
- Live FPS, 1% lows, DPC/ISR, GPU utilization, default-service baselines, and SSD/NVMe mapping are shown only when a trustworthy data source exists; 1.0 does not fabricate them.
- The x64 UI received an isolated startup smoke test. arm64 builds successfully but was not executed on arm64 hardware.
- Code signing requires a user-supplied certificate in CI secrets or a local secure certificate store.
