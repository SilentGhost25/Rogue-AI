# ROGUE AI: The Breach Protocol — Session Context & Architectural Memory

**Event Target Date:** 13 November 2026  
**Document Status:** **FINAL & FROZEN** (Revision 6.0 — Orchestrator & Attack Tree Complete)  
**System Target:** 50 Concurrent Campus Participants | Air-Gapped Campus LAN with AI Whitelist | Dual-Role Desktop Studio

---

## 1. Executive Architecture Overview

ROGUE AI: The Breach Protocol is a live cybersecurity competition platform where 50 participants defend an active Node.js web application (`harbour-notes`) against 8 scheduled multi-vector intrusions (00-warmup + 01–07 attacks) using AI coding agents (`agy` and `opencode`) and built-in manual code repair, monitored by an ASP.NET Core Control Plane and graded by a Python FastAPI multi-tier evaluator.

```
┌──────────────────────────────────────────────────────────────────────────┐
│                     CAMPUS LAN (AI Egress Whitelist)                     │
│                                                                          │
│   ┌──────────────────────────────────────────────────────────────────┐   │
│   │               CENTRAL CONTROL PLANE (Port 5080)                  │   │
│   │  • ASP.NET Core Minimal APIs + SQLite Append-Only Ledger         │   │
│   │  • Canonical Centipoint Hashing & Ed25519 Signing                │   │
│   │  • RogueAgentOrchestrator: Manual & Scheduled Attack Dispatch    │   │
│   │  • SignalR BreachHub (Admins, Projector, Arena, Canary, p:desks) │   │
│   │  • Desk Token Auth, Gated Payloads & Active Breach Reconcile     │   │
│   │  • Workspace Store (Encrypted snapshot storage)                  │   │
│   │  • Rotated Backups (20 rolling + hourly permanent snapshots)     │   │
│   └────────────────────────────────┬─────────────────────────────────┘   │
│                                    │ HTTP / REST (X-Internal-Token)      │
│   ┌────────────────────────────────▼─────────────────────────────────┐   │
│   │               BREACH EVALUATION ENGINE (Port 8000)               │   │
│   │  • Python FastAPI + Positive ACL Sandbox (.\BreachRunner)        │   │
│   │  • Multi-tier weighted grading (tier_checks) + ternary fallback  │   │
│   │  • Child-process CPU accounting (<300ms limit + JIT warm-up)     │   │
│   │  • Concurrency queue (Semaphore(4)) + Port squatting guard       │   │
│   │  • Safe degraded mode (no auto-execution unsandboxed on host)    │   │
│   └────────────────────────────────┬─────────────────────────────────┘   │
│                                    │ SignalR + REST (Token-gated)        │
│   ┌────────────────────────────────▼─────────────────────────────────┐   │
│   │               50x PARTICIPANT KIOSKS (WPF .NET 9)                │   │
│   │  • Microsoft ConPTY Native AI Terminal (`agy` / `opencode`)      │   │
│   │  • Embedded WebView2 live site view (`http://127.0.0.1:3000`)    │   │
│   │  • AvalonEdit In-App Code Editor (with auto-save on breach)      │   │
│   │  • Token-bucket SnapshotSync + Pre-landing local hash guard      │   │
│   │  • Pre-buffered Payload Download on BREACH_IMMINENT countdown    │   │
│   │  • Active breach reconciliation on launch / restart              │   │
│   │  • Top-bar Submission (F5), Local Test (F6), Queue Display       │   │
│   └──────────────────────────────────────────────────────────────────┘   │
└──────────────────────────────────────────────────────────────────────────┘
```

---

## 2. Frozen Specifications (v6.0 Decisions)

1. **Evaluator Sandboxing:** Positive least-privilege explicit ACLs on `.\BreachRunner` (read/execute on `node.exe`, write access strictly inside assigned ephemeral folder). Loopback defense via `X-Internal-Token`. Safe degraded mode halts auto-eval instead of running untrusted code unsandboxed on host.
2. **Child Process CPU Measurement (Attack 6):** Measured via `psutil.Process(child_pid).cpu_times()`, with 3 warm-up requests and a `<300ms` user CPU threshold.
3. **Collision-Proof Attack Landing & Pre-Buffering:**
   - Pre-landing hash verification on kiosk. Auto-save in-memory editor buffers at `BREACH_IMMINENT`.
   - Server broadcasts authoritative `landAtUtc` with 10-second imminent countdown window.
   - Kiosks pre-buffer payload archive over `/api/kiosk/{id}/attack-payload/{attackId}` during the countdown window and land simultaneously at `landAtUtc` ($\Delta < 200\text{ms}$).
4. **Token-Bucket Snapshot Limiter:** Burst capacity of 3 tokens, refill 1 per 5 seconds. System flushes bypass rate limiter.
5. **Centipoint Canonical Ledger Hashing:**
   - Pre-allocated sequence inside `BEGIN IMMEDIATE` transaction.
   - Points stored as integers: Base = 8500, Speed = 1500, Rejection = -300 (capped at -1500 per attack), Late = 5950.
   - Canonical pipe-delimited hashing across all columns: `prev_hash | id | participant_id | attack_id | component | centipoints | reason | run_id | evaluator_version | final | created_at_iso`.
   - Hourly public anchoring of chain head hash by independent organizer.
6. **Speed Bonus & Late Solve Math:**
   - Linear speed decay: $1500 \times (1 - \text{elapsed} / 30.0)$ over 30 min from participant's `landed_at`.
   - Late solve applies strictly if submitted after participant's own `landed_at` for Attack $N+1$ (or 17:30 for Attack 7). Awards 70% base (5950 centipoints) with 0 speed bonus.
7. **Submission Anti-Spam:** 1 in-flight submission per desk, 30s cooldown. Kiosk displays exact queue position.
8. **7-Hour Backup Retention:** 20 rolling 3-minute snapshots PLUS 1 permanent snapshot per hour, mirrored to external drive.
9. **Contingency Strategy:** No local GPU on host machine. Manual editing in AvalonEdit is the official competition contingency. Scoring is 100% neutral to AI vs. manual edits.
10. **Day 10 Go/No-Go Checklist:** Defined with 7 objective gates; if any fail, predetermined degraded paths are executed.
11. **Ephemeral Testing Protocol:** Standalone verification scripts created to test code during development must be deleted immediately after execution to keep the repository clean. Official test suites remain in `server/ControlPlane.Tests`, `engine/tests`, and `site/test`.
12. **Multi-Tier Partial Credit Support:** Evaluator inspects test script exports for `tier_checks()`. If present, applies tier weights proportionally against base points; otherwise falls back to binary ternary logic.

---

## 3. Day 1 Completed Deliverables & Acceptance Verification

| Deliverable | Status | Verification Command | Notes |
|---|---|---|---|
| **Dual Installer Split (Task 1.1)** | ✅ VERIFIED | `powershell -File installer/build-kiosk.ps1` | Dedicated `bin/KioskRelease` folder, clean build, packaging, and leak scan in one automated script. |
| **Negative Control Leak Test** | ✅ VERIFIED | `verify-kiosk-installer.ps1 -InstallerPath dist/RogueAI-Host-Setup.exe` | Fails loudly (exit code 1) detecting leaked attacks, solutions, engine, and server binaries. |
| **Host Setup Exclusions** | ✅ VERIFIED | `installer/host.iss` | Strict excludes for secrets, dev DB, and desk slips (`*.pem,*.key,secrets/*,data/*,*desk_tokens_slips*`). |
| **Desk Slips Outside Repo** | ✅ VERIFIED | `C:\BreachSecrets\desk_tokens_slips.csv` | Moved completely outside repository; gitignored. |
| **Sealed Master Key Backup** | ✅ VERIFIED | `C:\BreachSecrets\sealed_backup\` | Physical/external backup created with SHA-256 `MANIFEST.txt`. |
| **Full Token Gating Across API & Hub** | ✅ VERIFIED | Live HTTP `curl` & SignalR `BreachHub` | Gated snapshot, submit, telemetry, attack payload, and hub registration. Replay blocked (403). |
| **Live Attack Payload Gating** | ✅ VERIFIED | Live HTTP `curl` against port 5080 | 403 on pre-landing; 403 on token mismatch; 403 on missing token; 200 OK after landing. |
| **Live Backup Under Load + Mirroring** | ✅ VERIFIED | Live runtime logs | Periodic SQLite `VACUUM INTO` verified while server answering requests; USB mirror path added. |

---

## 4. Day 2 Completed Deliverables: Rogue Agent Orchestrator & Attack Tree

| Component | Status | Implementation Details | Acceptance Command |
|---|---|---|---|
| **Models (`AttackManifest`, `AttackDispatchState`)** | ✅ VERIFIED | [AttackManifest.cs](file:///c:/Users/Tarun%20J/Downloads/Rogue%20AI%20Round%202%20v2/server/ControlPlane/Models/AttackManifest.cs) with YAML deserializer, strict 0..N sequential IDs, tier weights summing to 100, and `< 2 MB` payload directory budget assertion. Enum `DispatchState` (`Pending`, `CanaryFired`, `CanaryConfirmed`, `ArenaImminent`, `ArenaLanded`, `Failed`). | `dotnet test server/ControlPlane.Tests` |
| **Database Schema & Methods** | ✅ VERIFIED | [Database.Orchestrator.cs](file:///c:/Users/Tarun%20J/Downloads/Rogue%20AI%20Round%202%20v2/server/ControlPlane/Data/Database.Orchestrator.cs) with `attack_dispatch` and `attack_dispatch_runtime` tables. Atomic methods: `GetDispatchState`, `EnsureDispatchRow`, `TransitionDispatch`, `PreregisterAttackDeliveries`, `MarkAttackLanded`, `IsAttackDeliveryPreRegistered`, `AllParticipantIds`, and `GetActiveAttackForReconcile`. | `dotnet test server/ControlPlane.Tests` |
| **Orchestrator BackgroundService** | ✅ VERIFIED | [RogueAgentOrchestrator.cs](file:///c:/Users/Tarun%20J/Downloads/Rogue%20AI%20Round%202%20v2/server/ControlPlane/Services/RogueAgentOrchestrator.cs) with `IClock` abstraction (`_clock.UtcNow`). Implements `FireToCanaryAsync`, `ConfirmCanary`, `FireToArenaAsync` (generating server-authoritative `landAtUtc`), `LandAtAsync`, and state validation. | `dotnet test server/ControlPlane.Tests` |
| **Host Admin Control Endpoints** | ✅ VERIFIED | [Program.cs](file:///c:/Users/Tarun%20J/Downloads/Rogue%20AI%20Round%202%20v2/server/ControlPlane/Program.cs):<br>• `GET /api/admin/attacks`<br>• `GET /api/admin/attacks/{id}/state`<br>• `POST /api/admin/attacks/{id}/canary`<br>• `POST /api/admin/attacks/{id}/confirm-canary`<br>• `POST /api/admin/attacks/{id}/fire-arena` (returns `landAtUtc`, enforces 409 conflict on duplicate/invalid state). | `dotnet test server/ControlPlane.Tests` |
| **Kiosk Gated Payload Delivery** | ✅ VERIFIED | `/api/kiosk/{id}/attack-payload/{attackId}`: Canonical gating allows downloads during `ArenaImminent` and `ArenaLanded` for pre-registered kiosks, and during `CanaryFired` strictly for the canary desk. | `dotnet test server/ControlPlane.Tests` |
| **Active Breach Reconciliation** | ✅ VERIFIED | `GET /api/kiosk/{id}/active-breach`: Queries `GetActiveAttackForReconcile()` returning current active breach, countdown timestamp, and landing state to recover late or rebooted kiosks. | `dotnet test server/ControlPlane.Tests` |
| **SignalR BreachHub Group Wiring** | ✅ VERIFIED | [BreachHub.cs](file:///c:/Users/Tarun%20J/Downloads/Rogue%20AI%20Round%202%20v2/server/ControlPlane/Hubs/BreachHub.cs): Kiosks join `arena` + `p:{deskId}`, canary desk joins `canary`, projector joins `arena`. Broadcasts support dual typed object and 3-primitive overloads. | `dotnet test server/ControlPlane.Tests` |
| **Attack Content Tree (`attacks/00-07`)** | ✅ VERIFIED | 8 full packages: `00-warmup`, `01-reflected-xss`, `02-stored-xss`, `03-sqli`, `04-csrf`, `05-idor`, `06-cpu-sabotage`, `07-supply-chain`. All include `manifest.yaml`, `hints.yaml`, `payload/`, and `tests/check.py` / `checks/check.py` with multi-tier checks. Dual Python-C# schema compatibility preserved. | `python -m unittest discover -s tests -t .` |
| **Evaluator Multi-Tier Execution** | ✅ VERIFIED | [evaluator.py](file:///c:/Users/Tarun%20J/Downloads/Rogue%20AI%20Round%202%20v2/engine/breach_engine/evaluator.py): Weighted tier execution via `tier_checks()` fallback to binary grading. | `python -m unittest discover -s tests -t .` |
| **Adversarial Sandbox Isolation** | ✅ VERIFIED | [test_sandbox_escape.py](file:///c:/Users/Tarun%20J/Downloads/Rogue%20AI%20Round%202%20v2/tests/security/test_sandbox_escape.py): Path traversal, token leak, environment scrubbing, fork bomb containment, and network outbound restrictions. | `python tests/security/test_sandbox_escape.py` |

---

## 5. Current Test & Build Status Summary

| Test Suite / Target | Command | Result |
|---|---|---|
| Test Suite / Target | Command | Result |
|---|---|---|
| **ControlPlane Tests** | `$env:DOTNET_ROOT = "$env:LOCALAPPDATA\dotnet"; $env:Path = "$env:DOTNET_ROOT;$env:Path"; dotnet test server/ControlPlane.Tests` | **29 Passed, 0 Failed, 0 Skipped** (100% green, includes restart recovery, overdue broadcast, in-countdown re-arm, unique index, and dual admin auth) |
| **LabKiosk Build** | `$env:DOTNET_ROOT = "$env:LOCALAPPDATA\dotnet"; $env:Path = "$env:DOTNET_ROOT;$env:Path"; dotnet build kiosk/LabKiosk` | **0 Warnings, 0 Errors** (Process-isolated WebView2, durable applied-marker, remote KioskConfig) |
| **Engine Tests** | `cd engine && python -m unittest discover -s tests -t .` | **10 Passed, 0 Failed** |
| **Sandbox Security Tests** | `python tests/security/test_sandbox_escape.py` | **5 Passed, 0 Failed** |
| **Harbour Notes Site** | `cd site && npm test` | **6 Passed, 0 Failed** |

---

## 6. Audit & Fixes Completed (Post-Fix Hardening)

1. **Durable Local Applied-Marker (Test 2 / Data Loss Prevention):**
   - Implemented in `kiosk/LabKiosk/MainWindow.xaml.cs`: when an attack lands and is written to disk, a durable marker `.breach-applied/attack-{attackId}.applied` is written in the participant's workspace.
   - `ApplyAttack()` checks for this marker; if present, it aborts extraction immediately to ensure a kiosk restart or reconnection never overwrites participant code fixes.
2. **Overdue Recovery SignalR Broadcast & Double-Fire Guard (Test 4):**
   - In `RogueAgentOrchestrator.StartAsync()`, when overdue `ArenaImminent` attacks are reconciled to `ArenaLanded`, `ATTACK_LANDED` is broadcast over SignalR to the `arena` group.
   - `LandAtAsync()` uses a `ConcurrentDictionary<int, byte> _armedLandings` to prevent duplicate timer scheduling.
   - State validation added in `LandAtAsync()` to abort if an attack is canceled or no longer `ArenaImminent`.
3. **In-Countdown Re-Arm Verified (Unit Tested):**
   - Added unit test `Startup_reconciles_ArenaImminent_in_countdown_rearm` in `RogueAgentOrchestratorTests.cs` verifying that mid-countdown restarts re-arm the timer and transition to `ArenaLanded` on expiration.
4. **Canary Desk ID Safety (`CANARY`):**
   - Updated `CanaryDeskId` in `appsettings.json`, `Program.cs`, and `BreachHub.cs` default from `"DESK-01"` to `"CANARY"`. Participant 1 (`DESK-01`) is no longer at risk of receiving early canary attack payloads.
5. **Simultaneity Clock Source Logging (Test 1 Instrumentation):**
   - Updated `MainWindow.xaml.cs` to explicitly log `[SIMULTANEITY] Attack #{id} payload written at {landingTimestamp:O} (Clock: SYNCED)` or `(Clock: UNSYNCED_FALLBACK)`.
6. **AI CLI Provisioning Automation (`scripts/provision-ai-clis.ps1`):**
   - Automated script installs `opencode-ai` via npm, ensures npm global bin is in User PATH, validates `%LOCALAPPDATA%\agy\bin`, and generates dev/test stubs if `agy.exe` is missing.
   - `MainWindow.xaml.cs` updated to launch `opencode` and `agy` directly or display actionable PowerShell instructions.
7. **Admin/Participant Separation & Dual-Auth (Multi-System Testing):**
   - **Kestrel Network Binding:** Bound to `0.0.0.0:5080` in `appsettings.json`.
   - **Firewall Rule (One-Time Setup on Control Plane Host):**
     `New-NetFirewallRule -DisplayName "RogueAI-ControlPlane" -Direction Inbound -Protocol TCP -LocalPort 5080 -Action Allow -Profile Private`
   - **Cookie Session Auth:** `AdminAuth.cs` registers cookie scheme `AdminSession` (8-hour non-sliding expiry, `SameSite=Strict`, `HttpOnly`).
   - **Dual-Auth Compatibility:** `AuthorizeAdmin` and `AdminAuth.AuthorizeAdminOrSession` accept either a valid session cookie OR a valid `X-Admin-Key` / `?key=`. Session cookie bypasses loopback restrictions from LAN IPs, while key auth retains loopback and rate-limiting.
   - **Admin Static Console SPA:** Zero-build frontend in `server/ControlPlane/wwwroot/admin/` (`login.html`, `index.html`, `admin.js`, `admin.css`) with real-time dispatch control and event logging.
   - **Remote Kiosk Configuration:** `KioskConfig.cs` parses `--server-url`, `--desk-id`, and `--desk-token` CLI args. When `--desk-token` is present, `MainWindow` calls `_api.EnrollAsync()` and wires SignalR with the desk token.
   - **Host Mode Deprecation:** `LabKiosk --host` redirects automatically to the browser admin console at `{server}/admin/` and terminates the kiosk window.
8. **Differentiated Attack Modals & Popups (Backlog):**
   - Customize intrusion alert dialogs and top-bar banner styling per attack vector (displaying `narrative.broadcast_title`, codename, targeted architectural layer, and tactical description from `manifest.yaml`).
   - Distinct visual themes and audio/haptic cues per severity band (Band 1: Defacement/XSS, Band 2: Stored/SQLi, Band 3: Auth/IDOR, Band 4: Sabotage/Supply Chain).


