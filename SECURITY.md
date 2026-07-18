# Security policy

Report vulnerabilities privately through GitHub Security Advisories. Do not include credentials, personal files, full logs, or public proof-of-concept details in an issue.

WinForge accepts no arbitrary shell from catalogs. The Worker validates the catalog hash, typed operation identity, connecting process PID, executable location, one-time nonce, and current-user named pipe. Cleaner roots are allowlisted and reparse points are blocked. Secrets and signing certificates must never be committed.
