# Testing matrix

| Area | Unit fixture | Read-only Windows integration | Isolated VM mutation |
|---|---:|---:|---:|
| Catalog/profile/compatibility | Yes | N/A | N/A |
| Registry absence/type rollback | In-memory adapter | Disabled | Required before release |
| Service/task restore | In-memory adapter | Disabled | Required before release |
| Cleaner allowlist/reparse | Temporary directory | N/A | Optional |
| Power parser/duplicate prevention | Fixed text and models | Disabled | Required before release |
| Worker/UAC/Named Pipe | Serialization and build | Disabled | Required before release |
| UI navigation/RTL | Resource tests + smoke | Isolated startup smoke | Manual accessibility pass |

Integration tests are skipped by default and never mutate the developer workstation.
