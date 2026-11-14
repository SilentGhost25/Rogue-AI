# ROGUE AI — AUDIT REPORT: BASELINE SYSTEM & REPOSITORY AUDIT
**Date:** November 13, 2026  
**Auditor:** Autonomous Systems Agent (Antigravity)  
**Target Repository:** ROGUE AI — The Breach Protocol  
**Active Branch:** `test` (Tracking `origin/test` at commit `c36a052`)  
**Audit Status:** STAGE 1 COMPLETE (READ-ONLY AUDIT)

---

## 1. Executive Summary

A comprehensive, read-only architectural, security, and dependency audit was performed across all six layers of the **ROGUE AI** competition platform:
1. **Base Website (`site/`):** Node.js Express & AEGIS Municipal Defense municipal infrastructure simulation.
2. **Breach Engine (`engine/`):** Python 3.12+ FastAPI dispatch, Win32 Job Object sandbox evaluation, scoring, and 33 attack payloads.
3. **Control Plane Server (`server/ControlPlane`):** ASP.NET Core 9.0, SQLite WAL database, SignalR real-time broadcast hub, backup service, dynamic LAN discovery.
4. **Participant Kiosk (`kiosk/LabKiosk`):** 3-pane Windows WPF kiosk hosting WebView2 (AEGIS site), AvalonEdit (code editor), and ConPTY terminal (AI CLIs: OpenCode / Agy).
5. **Simulator (`sim/`):** Concurrency load simulator (`fake_kiosks.py`) supporting up to 50 concurrent participant workloads.
6. **Infrastructure & Deployment (`infra/`, `installer/`, `scripts/`):** Docker Compose, Inno Setup installers, PowerShell automation.

All four native test suites currently compile and pass. However, **critical security vulnerabilities and operational hazards** exist in the Control Plane API authorization, SignalR hub connection gating, public tunnel exposure, and platform sandbox isolation that require remediation before live event deployment.

---

## 2. Git State & Branch Topology

- **Current Branch:** `test`
- **Head Commit:** `c36a052` (*"feat(attacks): restore original 22 attacks alongside the 10 multi-tier attacks (total 33 attacks)"*)
- **Remote:** `https://github.com/SilentGhost25/Rogue-AI.git`
- **Branch Relationships:**
  - `origin/master` (`62b7ad2`): Base competition release.
  - `origin/feature/ui-updates` (`5b4658c`): AEGIS futuristic UI overhaul and launcher rollforward fixes.
  - `origin/test` (`c36a052`): Integrates 33 attacks, 10-tier evaluation engine, evaluation result popup modal, anti-AI timestamp normalization, and host panel overhaul.
- **Untracked Local Files:**
  - `AGENT.md` (Local development guideline)
  - `installer/host.iss` (Standalone organizer host Inno Setup script)

---

## 3. Verified Test Execution Status

| Test Suite | Command | Execution Time | Results |
| :--- | :--- | :--- | :--- |
| **AEGIS Website** | `cd site && npm test` | 1.4s | **7 Passed, 0 Failed** |
| **Python Breach Engine** | `cd engine && python -m unittest discover -s tests -t .` | 131.8s | **41 Passed, 0 Failed** |
| **ControlPlane (.NET)** | `dotnet test server/ControlPlane.Tests` | 10.5s | **42 Passed, 0 Failed** |
| **LabKiosk Build** | `dotnet build kiosk/LabKiosk` | 10.5s | **Succeeded (0 Errors, 2 Warnings)** |
| **Sandbox Security Suite** | `python tests/security/test_sandbox_escape.py` | 14.7s | **5 Passed, 0 Failed** |

---

## 4. Prioritized Vulnerability & Defect Register

### Critical Defects (P0 — Event Blocking / Security Hazard)

1. **Unauthenticated Participant Snapshot Overwrite (`server/ControlPlane/Program.cs:304-315`):**
   * **Defect:** In `POST /api/kiosk/{id}/snapshot`, token validation is conditional on `token` being non-empty:
     ```csharp
     var token = httpReq.Headers["X-Kiosk-Token"].ToString();
     if (!string.IsNullOrWhiteSpace(token) && !db.ValidateDeskToken(token, id)) {
         return Results.StatusCode(403);
     }
     store.Save(id, req.Files, req.Deleted);
     ```
   * **Impact:** If `X-Kiosk-Token` is omitted or empty, the `if` check is bypassed entirely. Any attacker on the LAN can overwrite any participant's workspace files and corrupt their submissions.

2. **Unauthenticated Participant Code Disclosure (`server/ControlPlane/Program.cs:327-338`):**
   * **Defect:** Endpoints `GET /api/kiosk/{id}/files` and `POST /api/kiosk/{id}/files/fetch` only check whether `participantId` exists in the database.
   * **Impact:** Any participant can query the file listing and download the full source code and in-progress fixes of any rival desk by iterating participant IDs.

3. **SignalR Desk Token Bypass (`server/ControlPlane/Hubs/BreachHub.cs:20-30`):**
   * **Defect:** In `BreachHub.RegisterKiosk(string participantId, string token)`:
     ```csharp
     if (string.IsNullOrWhiteSpace(token) || !db.ValidateDeskToken(token, participantId)) {
         var participantExists = db.QueryScalar<long>("SELECT COUNT(*) FROM participants WHERE id = @p", ("@p", participantId)) > 0;
         if (!participantExists) { Context.Abort(); return; }
     }
     ```
   * **Impact:** If `token` is invalid or empty, the connection is only aborted if the participant does *not* exist. As long as `participantId` exists, an unauthenticated client is added to group `p:{participantId}` and receives real-time attack payloads and notifications.

4. **Public Exposure via Unauthenticated Tunnel (`share_online.bat`):**
   * **Defect:** `share_online.bat` launches `cloudflared.exe tunnel --url http://127.0.0.1:5080`.
   * **Impact:** Exposes the entire Control Plane to a public `trycloudflare.com` URL without network firewalling or IP restrictions. Combined with defects #1 and #2, external actors could compromise the live competition.

### High Defects (P1 — System Reliability / Isolation Risks)

5. **Sandbox Isolation Fails Open on Linux/Docker (`engine/breach_engine/evaluator.py:208-259`):**
   * **Defect:** `JobObjectSandbox` relies exclusively on Win32 API (`ctypes.windll.kernel32`). On Linux or inside Docker containers (`sys.platform != "win32"`), `self.handle = None` and `assign(proc)` returns `False` without raising an error or imposing limits.
   * **Impact:** Untrusted participant code runs uncontained on non-Windows host runtimes, violating fail-closed requirements.

6. **Missing Node.js in Engine Dockerfile (`engine/Dockerfile`):**
   * **Defect:** `engine/Dockerfile` installs only `python:3.12-slim` and `git`. It does not install Node.js or npm.
   * **Impact:** When the evaluator attempts to launch the participant's website (`node server.js`) inside Docker, the container crashes with `FileNotFoundError: node`.

7. **Default Hardcoded Production Credentials:**
   * **Control Plane Admin Key (`run.ps1:48`):** Falls back to `"LocalDevAdminKey_2026_Secure123"`.
   * **Admin Web Password (`appsettings.json:27`):** BCrypt hash defaults to `"admin123456"`.
   * **GitHub Remote URL (`server/.../GitMirrorService.cs:24`):** Hardcoded fallback to `"https://github.com/Prince649294u83/Rogue-AI-competition.git"`.

### Medium Defects (P2 — Operational & Configuration Risks)

8. **Missing Inno Setup Build Target (`installer/setup.iss:47`):**
   * **Defect:** `setup.iss` references `..\kiosk\LabKiosk\bin\SelfContained\*`, but that directory is not generated during normal `dotnet build`.
   * **Impact:** Building the participant installer fails unless `dotnet publish -c Release -r win-x64 --self-contained` is run explicitly beforehand.

9. **Hardcoded Machine-Specific Paths in Setup Scripts:**
   * `scripts/setup-sandbox-accounts.ps1:18`: `[string]$ProjectRoot = "C:\Users\Tarun J\Downloads\Rogue AI Round 2 v2"`
   * `scripts/clean-old.ps1:52` & `scripts/uninstall-complete.ps1:306`: Hardcoded `C:\Users\dell\OneDrive\Desktop`.
   * **Impact:** Admin provisioning scripts fail or alter unintended directories if executed on other laptops.

---

## 5. Exit Gate Evaluation: STAGE 1

| Acceptance Criteria | Status | Notes |
| :--- | :--- | :--- |
| **Read-Only Inspection Enforced** | **PASSED** | No source code, databases, or configurations were modified. |
| **All System Components Identified** | **PASSED** | Node site, Python engine, C# ControlPlane, WPF Kiosk, simulator, and scripts mapped. |
| **Communication Protocols Mapped** | **PASSED** | Detailed in `ARCHITECTURE_MAP.md`. |
| **Dependencies & Environment Mapped** | **PASSED** | Detailed in `DEPENDENCY_REPORT.md`. |
| **Pre-existing Tests Verified** | **PASSED** | 95 unit/integration tests executed and verified passing. |
| **Defect Register Documented** | **PASSED** | 4 Critical, 3 High, 2 Medium defects identified and prioritized. |

**Stage 1 Exit Gate is SATISFIED.** Ready for Stage 2 Root-Cause Analysis and Implementation Planning upon user authorization.

