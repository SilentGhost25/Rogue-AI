# ROGUE AI — INTEGRATION & FUNCTIONAL VERIFICATION REPORT (STAGE 4)

**Document:** System Integration, Cross-Service Communication, and Reliability Verification  
**Date:** November 14, 2026  
**Auditor:** Autonomous Systems Agent (Antigravity)  
**Status:** STAGE 4 COMPLETE — ALL APPLICATION & DEPLOYMENT COMPONENTS INTEGRATED  

---

## 1. Executive Summary

In Stage 4, all platform integration, deployment pipelines, configuration defaults, and packaging scripts were repaired and verified:
1. **Containerized Evaluator Runtime (`engine/Dockerfile`):** Added Node.js 22 LTS, npm, curl, and pre-cached `site/` dependencies into the Docker build definition, ensuring containerized evaluations run reliably without missing binary errors.
2. **Environment & Script Sanitization (`scripts/`):** Removed hardcoded personal developer machine paths (`Tarun J`, `dell`) across `setup-sandbox-accounts.ps1`, `clean-old.ps1`, and `uninstall-complete.ps1`, replacing them with dynamic `$PSScriptRoot` and Windows environment lookups.
3. **Git Mirroring Safeguard (`server/ControlPlane/Services/GitMirrorService.cs`):** Removed the hardcoded fallback GitHub repository URL (`https://github.com/Prince649294u83/Rogue-AI-competition.git`). Mirroring is now cleanly disabled if no organizer remote URL is configured, avoiding accidental push attempts to unowned repositories.
4. **Automated Packaging Pipeline (`installer/build-kiosk.ps1`):** Verified automated self-contained publication of LabKiosk (.NET 9 self-contained bundle with bundled runtime) and ControlPlane, resolving the missing `bin\SelfContained` dependency for Inno Setup compiler (`setup.iss`).
5. **Attack Manifest Audit (33 Attacks):** Audited all 33 manifests (`00-warmup` through `32-janus-knot`). Confirmed all 33 manifests specify `dispatch.mode: manual`, strictly preserving organizer manual control over attack dispatches.
6. **Concurrent Simulation Regression:** Executed simulated multi-kiosk stress test (`sim/fake_kiosks.py`) against live Control Plane. Verified registration, token gating, snapshot synchronization, AI telemetry logging, and fix submission queueing under multi-threaded concurrency.

---

## 2. Component Integration Matrix

| Integration Interface | Source -> Target | Protocol / Mechanism | Verification Status | Notes |
| :--- | :--- | :--- | :--- | :--- |
| **Kiosk -> Control Plane** | LabKiosk -> ControlPlane:5080 | HTTP REST (`X-Kiosk-Token`) | **VERIFIED PASS** | Token enforced on all participant routes |
| **SignalR Broadcast Hub** | LabKiosk <-> BreachHub:5080 | WSS WebSockets | **VERIFIED PASS** | Strict desk token gating on registration |
| **Local Site Hosting** | LabKiosk -> `site/server.js` | HTTP 3000 (`node --watch`) | **VERIFIED PASS** | Evaluator & WebView2 render cleanly |
| **Engine Evaluation** | ControlPlane:5080 -> Engine:8000 | HTTP REST (`/evaluate`) | **VERIFIED PASS** | Semaphore(4) slot allocation verified |
| **LAN Auto-Discovery** | LabKiosk -> Host:5082 | UDP Broadcast / Unicast Ping | **VERIFIED PASS** | Beacon response returns host LAN IP |
| **Database Persistence** | ControlPlane -> `breach.db` | SQLite WAL Mode + Triggers | **VERIFIED PASS** | Append-only ledger & event logs verified |
| **Automated Backups** | BackupService -> `data/backups/` | SQLite Online Backup API | **VERIFIED PASS** | Hourly snapshots created automatically |
| **Packaging & Installers** | `build-kiosk.ps1` -> `dist/` | Inno Setup + Dotnet Publish | **VERIFIED PASS** | Self-contained bundles staged properly |

---

## 3. Attack Manifest Audit Summary (All 33 Attacks)

Every attack manifest in `attacks/` was inspected:
* **Attack Count:** 33 manifests (`00-warmup` to `32-janus-knot`).
* **Sequence & IDs:** Exactly sequential (IDs 0 through 32).
* **Scoring Weights:** All weight distributions sum to 100 (`speed + performance + prompt_efficiency + quality + resource = 100`).
* **Dispatch Mode:** **100% of attacks are configured with `dispatch.mode: manual`.** No attack is configured for unauthorized automatic firing, adhering strictly to competition rules.

---

## 4. Concurrency Simulation Test Results

```
[*] Starting stress test: 10 kiosks against http://127.0.0.1:5080 (eval_load=False)
[+] Control plane online. Paused: False, Server time: 2026-11-13T19:07:29Z
[+] Loaded 10 desk tokens from server/ControlPlane/data/breach.db

[+] Concurrency Test Complete in 0.97s:
    - Registered:         10 / 10 (100% success)
    - Snapshots written:  20 (100% success)
    - Telemetry recorded: 30 events
    - Submissions queued: 10 (100% success)

[+] Public Leaderboard: 11 entries ranked (Frozen: False)
    Top Defender: Rank #1 - Defender 05, Solved: 0
```

---

## 5. Exit Gate Evaluation: STAGE 4

| Integration Acceptance Gate | Status | Evidence |
| :--- | :--- | :--- |
| **All application components start & communicate** | **SATISFIED** | Live ControlPlane, Engine, Site, and Kiosks verified. |
| **Docker container dependencies complete** | **SATISFIED** | Node.js 22 LTS & npm installed in `engine/Dockerfile`. |
| **Setup scripts sanitized of machine paths** | **SATISFIED** | `$PSScriptRoot` dynamic paths implemented. |
| **All 33 attack manifests validated** | **SATISFIED** | 33/33 loaded, weights validated, manual mode confirmed. |
| **Automated regression simulation active** | **SATISFIED** | `scripts/run_simulation.ps1` runs clean in < 1s. |

**Stage 4 Exit Gate is SATISFIED.** Ready to begin Stage 5 (Full Validation, Security Testing & Capacity Testing) upon authorization.

