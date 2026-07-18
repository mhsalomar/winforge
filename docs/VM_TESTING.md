# VM testing

Use a disposable Windows Sandbox or snapshot-capable Windows 11 VM. Copy the portable package, create a restore point, and set `WINFORGE_RUN_INTEGRATION=1` only inside the VM.

Validate: non-admin scan, Cleaner Preview, a temporary HKCU test value apply/verify/rollback, absent-value rollback, service/task fixtures created for the test, UAC only for HKLM/admin plans, crash during a batch, next-launch journal discovery, power-plan creation without duplicates, restore to Windows Balanced, Appx/feature enumeration, Arabic RTL, support bundle, and uninstall/portable cleanup. Never use Defender, Update, Firewall, anti-cheat, or development processes as fixtures.
