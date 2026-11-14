# ROGUE AI: The Breach Protocol — Event Day Runbook

**Mission Role:** Event Director, Lead Proctor & System Operator  
**Event Timeline Reference:** 13 November 2026 Season  
**Operational Scope:** Live Event Execution, Wave Dispatch, Monitoring, and Conclusion

---

## 1. Master Event Schedule & Operational Phases

```
┌────────────┬─────────────┬──────────────┬──────────────┬──────────────┬────────────┐
│  T - 90m   │   T - 30m   │   T = 00m    │   T + 15m    │   T + 120m   │  T + 150m  │
│ Host Setup │ Participant │ Event Start  │ Attack Waves │ Scoreboard   │   Awards   │
│ & Sanity   │ Check-in    │ Kickoff      │ 1 through 4  │ Freeze       │ & Teardown │
└────────────┴─────────────┴──────────────┴──────────────┴──────────────┴────────────┘
```

---

## 2. Phase-by-Phase Execution Runbook

### Phase 1: Pre-Flight Initialization (T - 90m to T - 45m)
1. **Network Activation:**
   * Power on venue router/switch.
   * Verify DHCP pool has at least 80 addresses available (`.10` to `.150`).
   * Confirm AP Isolation is **OFF**.
2. **Organizer Host Startup:**
   * Boot Organizer Host laptop/desktop.
   * Connect to network via Ethernet cable (recommended) or dedicated 5GHz Wi-Fi.
   * Note Host IP using `ipconfig` (e.g., `192.168.1.100`).
   * Open Administrator Command Prompt:
     ```cmd
     cd C:\RogueAI
     .\run.bat host
     ```
3. **Display Configuration:**
   * Connect secondary monitor/projector.
   * Open full-screen browser to:
     ```
     http://localhost:5080/leaderboard
     ```
   * Press `F11` for presentation mode.
4. **Proctor Smoke Test:**
   * Boot 1 staging kiosk machine.
   * Launch `LabKiosk.exe`.
   * Register as `DESK-TEST`. Confirm connection appears on Admin Dashboard.
   * Purge test data from Admin Console or run:
     ```powershell
     Invoke-RestMethod -Uri "http://localhost:5080/api/admin/reset" -Method Post -Headers @{"X-Admin-Key"="event_admin_secure_key_2026"}
     ```

---

### Phase 2: Participant Check-In & Onboarding (T - 45m to T - 05m)
1. **Desk Preparation:**
   * Ensure each participant station has:
     * Machine powered on and connected to venue network.
     * Desk card with designated ID (`DESK-01` to `DESK-50`).
     * Shortcut to **Rogue AI LabKiosk** on desktop.
2. **Participant Seating & Launch:**
   * As participants arrive, direct them to their assigned desk.
   * Instruct them to open **LabKiosk**.
   * Onboarding screen prompt:
     * Participant ID: `DESK-XX`
     * Callsign / Handle: `<Participant Name or Handle>`
   * Click **Join Breach Simulation**.
3. **Monitor Fleet Status:**
   * Open `http://localhost:5080/admin/` on Organizer Host.
   * Watch the **Active Kiosks** counter reach expected count (e.g., 50).
   * Green status dots confirm active SignalR heartbeats.

---

### Phase 3: Kickoff & Opening Briefing (T - 05m to T = 00m)
1. Direct participant attention to projector / podium.
2. Deliver the participant orientation briefing:
   * **Target:** Secure the AEGIS Enterprise portal located in `C:\BreachWorkspace`.
   * **Rule 1:** All code changes must remain in `C:\BreachWorkspace`.
   * **Rule 2:** The built-in AI assistant CLI (`claude`, `gemini`, etc.) is available via kiosk terminal.
   * **Rule 3:** The automated Rogue AI adversary will launch periodic breaches against your application.
   * **Rule 4:** Submit fixes via the **Deploy Patch** button on the kiosk header.
3. At `T = 00m`, broadcast the start announcement via Admin Console:
   * Action: **Broadcast Announcement**
   * Message: `BREACH PROTOCOL INITIATED. DEFEND YOUR SYSTEMS.`

---

### Phase 4: Attack Wave Management (T + 15m to T + 120m)

All 33 attacks are configured in `attacks/` under `mode: manual`. Dispatches are executed directly by the organizer team.

```
Wave 1 (T+15m): 01-sql-injection-auth, 02-broken-object-level-auth, 03-hardcoded-api-secrets
Wave 2 (T+45m): 05-path-traversal-downloads, 06-ssrf-webhook-handler, 09-idor-order-receipts
Wave 3 (T+75m): 12-jwt-algorithm-confusion, 15-prototype-pollution-reports, 18-insecure-deserialization
Wave 4 (T+105m): 24-race-condition-voucher, 29-memory-corruption-parser, 33-zero-day-chain
```

#### How to Dispatch an Attack Wave:
1. Navigate to `http://localhost:5080/admin/` -> **Attack Dispatcher**.
2. Select target attack from the dropdown (e.g., `01-sql-injection-auth`).
3. Select Target:
   * **All Kiosks** (Standard wave).
   * Or **Specific Desk** (for targeted re-evaluation or handicap testing).
4. Click **Dispatch Attack**.
5. **Observed System Reaction:**
   * Control Plane logs event: `ATTACK_DISPATCHED`.
   * SignalR broadcasts instant notification to all kiosks: red alert banner on LabKiosk.
   * Kiosks trigger local vulnerability reproduction tests against `http://localhost:3000`.
   * Participants analyze exploit logs, identify vulnerability, and modify code.
   * Participants click **Submit Patch** -> Control Plane coordinates with Engine on port 8000 -> Evaluator runs acceptance tests -> Points credited to `score_ledger`.

---

### Phase 5: Scoreboard Freeze & Final Countdown (T + 120m to T + 150m)
1. **At T + 120m (30 minutes remaining):**
   * Activate **Scoreboard Freeze**:
     * In Admin Console, toggle **Leaderboard Freeze: ENABLED**.
     * Projector screen displays frozen ranks with a pulsing `SCOREBOARD FROZEN` indicator.
     * Participant submissions continue to be evaluated and logged in the append-only SQLite ledger, but public ranks do not update.
2. **At T + 140m (10 minutes remaining):**
   * Send 10-minute warning broadcast: `10 MINUTES REMAINING. FINALIZE ALL PATCHES.`
3. **At T + 149m (1 minute remaining):**
   * Proctor conducts countdown aloud.
4. **At T + 150m (Event Conclusion):**
   * In Admin Console, click **Emergency / Lockdown Mode** or **Pause Submissions**.
   * All kiosk submit buttons are disabled instantly via SignalR.
   * Broadcast final message: `SIMULATION TERMINATED. STEP AWAY FROM KEYBOARDS.`

---

### Phase 6: Winner Certification & Presentation
1. **Unfreeze & Audit:**
   * In Admin Console, click **Export Ledger (CSV)**.
   * Save `score_ledger_<timestamp>.csv` for event records.
   * Inspect top 3 finishers for anomalies or unverified score surges.
2. **Leaderboard Reveal:**
   * Switch projector screen to the Live Final Leaderboard.
   * In Admin Console, toggle **Leaderboard Freeze: DISABLED**.
   * Audience views animated reveal of 1st, 2nd, and 3rd place winners.
3. **Award Ceremony:**
   * Announce winners and distribute awards.

---

### Phase 7: Post-Event Teardown & Workspace Sanitize
1. On Organizer Host, gracefully terminate host process:
   * Press `Ctrl+C` in the command window.
   * Confirm shutdown of Control Plane and Breach Engine.
2. Back up final database:
   * Copy `server\ControlPlane\data\controlplane.db` to secure external storage.
3. On Participant Kiosks:
   * Run cleanup script to remove participant workspaces:
     ```powershell
     powershell -ExecutionPolicy Bypass -File scripts\uninstall-complete.ps1
     ```
   * Deletes `C:\BreachWorkspace`, temporary files, and desktop shortcuts.

