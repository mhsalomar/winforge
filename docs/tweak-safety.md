# Tweak safety

| Risk | Meaning | Default behavior |
|---|---|---|
| Low | Documented, local, reversible | May appear in Recommended |
| Medium | Functional trade-off or admin policy | Optional and separately reviewed |
| High | Broad functional impact | Never selected automatically |
| Expert | Security, boot, destructive, or poorly portable | Read-only explanation or blocked |

WinForge does not implement registry cleaning, Prefetch deletion as a performance tweak, page-file disabling, Defender/Firewall/Update disabling, BCD timer changes, HPET disabling, arbitrary MMCSS values, global TCP magic values, IPv6 disabling, Realtime game priority, automatic MSI mode, WinSxS deletion, or remote `iwr/irm | iex` execution.

Expected impact is descriptive, not a performance claim. Gaming changes preserve Defender, Windows Update, Store, Xbox/Gaming Services, Vanguard, and other anti-cheat systems.
