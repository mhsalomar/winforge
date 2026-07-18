# Architecture

WinForge uses four deep modules at explicit seams:

- **UI**: WPF/MVVM, normal-user inspection, previews, plan review, and local presentation. It never writes HKLM or changes services directly.
- **Core**: typed operations, catalog/profile validation, compatibility, planning, journaling contracts, and batch rollback behavior.
- **Infrastructure**: Windows adapters for registry, processes, files, startup, services, tasks, Appx, features, power, network, and reports.
- **Worker**: a separate `asInvoker` executable. The UI launches it with `runas` only for administrative plans. A current-user named pipe, one-time nonce, client PID/path check, catalog SHA-256, and exact plan/catalog comparison protect the seam.

Long-running work is asynchronous and cancellable. Catalogs cannot contain arbitrary PowerShell. Read-only PowerShell queries and repair commands are hard-coded allowlists using `ProcessStartInfo.ArgumentList` and timeouts.

The journal is written before mutation. Registry state records existence, value kind, and exact value. Service and scheduled-task operations capture their original state. On a failed verification, completed operations roll back in reverse order.
