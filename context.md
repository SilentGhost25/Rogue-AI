# ROGUE AI: The Breach Protocol — Session Context & Architectural Memory

**Event Target Date:** 13 November 2026  
**Document Status:** **FINAL & FROZEN** (Revision 5.0)  
**System Target:** 50 Concurrent Campus Participants | Air-Gapped Campus LAN with AI Whitelist | Dual-Role Desktop Studio

---

## 1. Executive Architecture Overview

ROGUE AI: The Breach Protocol is a live cybersecurity competition platform where 50 participants defend an active Node.js web application (`harbour-notes`) against 7 scheduled multi-vector intrusions using AI coding agents (`agy` and `opencode`) and built-in manual code repair, monitored by an ASP.NET Core Control Plane and graded by a Python FastAPI multi-tier evaluator.

```
┌──────────────────────────────────────────────────────────────────────────┐
│                     CAMPUS LAN (AI Egress Whitelist)                     │
│                                                                          │
│   ┌──────────────────────────────────────────────────────────────────┐   │
│   │               CENTRAL CONTROL PLANE (Port 5080)                  │   │
│   │  • ASP.NET Core Minimal APIs + SQLite Append-Only Ledger         │   │
│   │  • Canonical Centipoint Hashing & Ed25519 Signing                │   │
│   │  • Desk Token Auth, Gated Payloads & Submission Anti-Spam        │   │
│   │  • SignalR BreachHub (Admins, Projector, Participant Groups)     │   │
│   │  • Workspace Store (Encrypted snapshot storage)                  │   │
│   │  • Rotated Backups (20 rolling + hourly permanent snapshots)     │   │
│   └────────────────────────────────┬─────────────────────────────────┘   │
│                                    │ HTTP / REST (X-Internal-Token)      │
│   ┌────────────────────────────────▼─────────────────────────────────┐   │
│   │               BREACH EVALUATION ENGINE (Port 8000)               │   │
│   │  • Python FastAPI + Positive ACL Sandbox (.\BreachRunner)        │   │
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
│   │  • Top-bar Submission (F5), Local Test (F6), Queue Display       │   │
│   └──────────────────────────────────────────────────────────────────┘   │
└──────────────────────────────────────────────────────────────────────────┘
```

---

## 2. Frozen Specifications (v5.0 Decisions)

1. **Evaluator Sandboxing:** Positive least-privilege explicit ACLs on `.\BreachRunner` (read/execute on `node.exe`, write access strictly inside assigned ephemeral folder). Loopback defense via `X-Internal-Token`. Safe degraded mode halts auto-eval instead of running untrusted code unsandboxed on host.
2. **Child Process CPU Measurement (Attack 6):** Measured via `psutil.Process(child_pid).cpu_times()`, with 3 warm-up requests and a `<300ms` user CPU threshold.
3. **Collision-Proof Attack Landing:** Pre-landing hash verification on kiosk. Auto-save in-memory editor buffers at `BREACH_IMMINENT`. Retry loop with local backup on persistent conflict.
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
