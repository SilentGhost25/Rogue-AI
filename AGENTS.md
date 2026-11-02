# AGENTS.md: rules every coding agent reads first

Project: ROGUE AI, The Breach Protocol (event 13 Nov 2026). Full plan: `docs/build-plan.md`.

## Rules
- Pin all versions. Never invent a NuGet or PyPI package name: verify it exists on nuget.org or pypi.org before adding.
  (Versions in the `.csproj` files are best guesses and must be checked before the first restore.)
- One task per change, with tests. Do not move on until the task's acceptance command passes.
- Log every state change to the event log. No secrets in the repo.
- Windows-first paths for the kiosk. Treat `contracts/` as frozen unless the plan changes.
- The server owns Git. Kiosks never run Git and never hold credentials.
- `event_log` and `score_ledger` are append-only (enforced by SQLite triggers). Corrections are new entries.
- Event accounts must never be used for building or piloting.

## Layout
| Folder | What | Acceptance command |
| --- | --- | --- |
| `site/` | Base website (Node 22.13+, Express, built-in SQLite) | `cd site && npm install && npm test` |
| `engine/` | Python: attack apply, evaluator, scoring, FastAPI | `cd engine && python -m unittest discover -s tests -t .` |
| `server/` | ASP.NET Core control plane, SignalR hub, admin page | `dotnet test server/ControlPlane.Tests` |
| `kiosk/` | WPF LabKiosk (Windows only) | `dotnet build kiosk/LabKiosk` |
| `attacks/` | Seven attack pack scaffolds (`manifest.yaml` only for now) | covered by engine tests |
| `sim/` | 50 fake kiosks (REST only) | `python sim/fake_kiosks.py --count 50` |
| `infra/` | docker-compose: Gitea, engine | `docker compose -f infra/docker-compose.yml up -d` |
| `contracts/` | OpenAPI, event schema, attack manifest schema | n/a |

## Security-critical code (human review line by line)
Snapshot sync (`kiosk/.../SnapshotSync.cs`, `server/.../WorkspaceStore.cs`), attack landing (`engine/.../patching.py`,
`AttackDispatcher.cs`), lockdown, scoring, evaluator isolation.
