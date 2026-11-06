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
| `site/` | Base website (Node 22.13+, zero dependencies, built-in SQLite) | `cd site && npm test` |
| `engine/` | Python: attack apply, evaluator, checks, scoring, FastAPI | `cd engine && python -m unittest discover -s tests -t .` |
| `server/` | ASP.NET Core 9 control plane, SignalR hub, Command Center and projector pages | `dotnet test server/ControlPlane.Tests` |
| `kiosk/` | WPF LabKiosk, .NET 9 (Windows only): kiosk mode and `--host` organizer mode | `dotnet build kiosk/LabKiosk` |
| `attacks/` | Seven attack packs. Edit `attacks/_build.py`, then run it: never hand-edit generated files | engine tests |
| `sim/` | 50 fake kiosks (REST only) | `python sim/fake_kiosks.py --count 50` |
| `infra/` | docker-compose: Gitea, engine | `docker compose -f infra/docker-compose.yml up -d` |
| `contracts/` | OpenAPI, event schema, attack manifest schema | n/a |

## Security-critical code (human review line by line)
Snapshot sync (`kiosk/.../SnapshotSync.cs`, `server/.../WorkspaceStore.cs`), attack landing (`engine/.../patching.py`,
`AttackDispatcher.cs`), lockdown, scoring, evaluator isolation.

## Rules added with the terminal work
- The terminal runs only the fixed programs from `kiosk.json` (agy, `opencode attach`). Never add a general shell.
- Attack packs must touch disjoint files, so stacked attacks never conflict (see `attacks/_build.py`).
- A new attack needs: transform in `_build.py`, a check in `engine/breach_engine/checks.py`, and it must pass the matrix test.
- Evaluator checks live only in `engine/`; never copy them to a kiosk or into a workspace.

## Revision 3 rules
- Scoring is +85 base, +15 max time bonus, -3 per rejected patch, -0.2 per prompt past the threshold. Change it only in
  `engine/breach_engine/scoring.py` (and its tests), never in the server.
- Attacks may share files only if their hunks stay apart (2 and 3 share search.js; 4 and 6 share api.js). The matrix test
  `test_full_event_sequence_stacks_without_conflicts` must stay green.
- Never state a build or test result you did not run. The plan's section 14 and 16 numbers are not measured results.
