# ROGUE AI: The Breach Protocol — Event Operations Runbook

**Mission Title:** Live Operations Field Manual  
**Operating Console:** `http://localhost:5080/admin/`  
**Host Launch Command:** `.\run.bat host`  
**Target Event Workload:** 50 Participants + 4 Administrators

---

## 1. Master Event Operations Timeline

```
T - 90m ── [ Infrastructure ] Power on router, set Host static IP, verify firewall
T - 60m ── [ Host Startup   ] Start .\run.bat host, verify Admin Console & Engine
T - 45m ── [ Staging Test   ] Connect test kiosk, test patch deploy, verify SignalR
T - 30m ── [ Test Clean     ] Execute Attack Reset to clear test deliveries
T - 20m ── [ Seating        ] Seat participants, launch kiosks, monitor check-in
T = 00m ── [ Kickoff        ] Broadcast start announcement, start competition timer
T + 15m ── [ Wave 1 Fire    ] Dispatch Band 1 attacks (Attacks 0, 1, 2, 3)
T + 45m ── [ Wave 2 Fire    ] Dispatch Band 2 attacks (Attacks 4, 5, 6, 7)
T + 75m ── [ Wave 3 Fire    ] Dispatch Band 3 attacks (Attacks 8, 9, 10)
T + 105m ─ [ Wave 4 Fire    ] Dispatch Band 4/5 Boss Attack (Attack 11)
T + 120m ─ [ Freeze         ] Activate Scoreboard Freeze (Submissions stay open)
T + 150m ─ [ Event End      ] Lock submissions, export score CSV, reveal winners
```

---

## 2. Pre-Event Operations & Clean-Slate Reset

### 2.1 Starting the Organizer Host Machine
1. Open PowerShell / Command Prompt as Administrator on the Organizer Laptop.
2. Navigate to project root:
   ```cmd
   cd C:\RogueAI
   .\run.bat host
   ```
3. Confirm console startup logs:
   ```
   [+] Python Breach Engine listening on http://127.0.0.1:8000
   [+] Control Plane listening on http://0.0.0.0:5080
   [+] UDP Discovery Beacon broadcasting on port 5082
   ```

### 2.2 Accessing the Admin Console
1. Open Google Chrome or Microsoft Edge.
2. Navigate to:
   ```
   http://localhost:5080/admin/
   ```
3. Login using the configured administrator credentials:
   * **Username:** `admin`
   * **Password:** (Configured during initial setup or set via `ADMIN_TOKEN`)

### 2.3 Clearing Stale Test Data (CRITICAL STEP)
Before participants sit down, ensure any test attacks or test deliveries from rehearsals are completely purged:

* **To clear prior test attack badges from all participant screens:**
  Run in an administrative PowerShell terminal:
  ```powershell
  Invoke-RestMethod -Uri "http://localhost:5080/api/admin/attacks/reset-all" -Method Post -Headers @{"X-Admin-Key"="event_admin_secure_key_2026"}
  ```
* **To perform a 100% clean session reset (Purges test scores, test kiosks, and starts fresh):**
  ```powershell
  Invoke-RestMethod -Uri "http://localhost:5080/api/admin/reset-session" -Method Post -Headers @{"X-Admin-Key"="event_admin_secure_key_2026"}
  ```
* **Verification:** Refresh Admin Console. Active Kiosks count resets to `0`, and all attack states display `Pending`.

---

## 3. Participant Check-In & Onboarding Phase (T-30m to T-0m)

1. **Verify Desks:** Each desk should have a card displaying:
   * **Desk ID:** E.g., `DESK-01`
   * **Host IP:** E.g., `http://192.168.1.100:5080` (for manual fallback if needed)
2. **Participant Action:** Participants open **Rogue AI LabKiosk**.
   * Onboarding window detects the host automatically.
   * Participant enters Desk ID and Callsign, clicks **Join Breach Simulation**.
3. **Admin Monitoring:**
   * Watch the **Active Kiosks & Desks** counter on the Admin Console (`http://localhost:5080/admin/`).
   * Confirm that each desk appears in the table with a green status indicator and station IP.
   * Verify that the purple-themed AEGIS command center renders on participant screens.

---

## 4. Live Competition & Attack Wave Dispatch (T=0m to T+120m)

### 4.1 Competition Kickoff
1. Send an arena-wide announcement:
   ```powershell
   $body = @{ message = "BREACH PROTOCOL INITIATED. DEFEND THE AEGIS CORE." } | ConvertTo-Json
   Invoke-RestMethod -Uri "http://localhost:5080/admin/broadcast" -Method Post -Body $body -ContentType "application/json" -Headers @{"X-Admin-Key"="event_admin_secure_key_2026"}
   ```
2. Direct audience to the live projector screen displaying `http://<HOST_IP>:5080/leaderboard`. Press `F11` for full screen.

### 4.2 Dispatching Attack Waves via Admin UI

#### Wave 1: Band 1 Foundation Breaches (T + 15m)
* **Attack #0 (Warmup Calibration):**
  1. Locate row `#0` in the **Attack Dispatch State Machine** table.
  2. Click **Fire Arena**.
  3. Confirm the modal prompt (`OK`).
  4. Watch kiosks turn amber as the 10-second warning sounds, then red as the incident badge lands.
* **Attack #1 (Portal Integrity / Reflected XSS):**
  1. Locate row `#1`. Click **Fire Arena**.
* **Attack #2 (Feed Rendering / Stored XSS):**
  1. Locate row `#2`. Click **Fire Arena**.

#### Wave 2: Band 2 Deep Data Breaches (T + 45m)
* **Attack #3 (SQL Injection / Data Layer):** Locate row `#3`, click **Fire Arena**.
* **Attack #4 (CSRF / Dispatch Control):** Locate row `#4`, click **Fire Arena**.
* **Attack #5 (IDOR / Vault Boundary):** Locate row `#5`, click **Fire Arena**.

#### Wave 3: Band 3 & 4 Architectural Failures (T + 75m)
* **Attack #8 (Cookie Auth Integrity):** Click **Fire Arena**.
* **Attack #9 (Mass Assignment / Payload Paradox):** Click **Fire Arena**.
* **Attack #10 (Path Traversal / Asset Boundary):** Click **Fire Arena**.

#### Wave 4: The Final Boss Attack (T + 105m)
* At T+105m, announce the final crisis over the PA system.
* In the top-right header, click the **🔥 Fire Boss Attack** button.
* Confirm the warning prompt:
  ```
  ☠️ BOSS ATTACK FIRED: Attack #11 (Session Fracture) dispatched to all kiosks!
  ```
* All kiosks receive the catastrophic Session Fracture breach.

---

## 5. Live Monitoring & Participant Triage

### 5.1 Reading the Participant Scorecard Table
* **Local Penalties:** If a desk has high penalties (e.g., `8 fail (-24.0 pts)`), they are running failing local tests before fixing root issues.
* **Attack Columns (#0..#8):**
  * `PASS` (Green): Solved and verified by Python sandbox.
  * `FAIL` (Red): Fix submitted, but tests failed. Hover over `FAIL` or inspect **Diagnostic Verdict** column to see why.
* **Audit Button:** Click **Audit** on any row to view the participant's detailed 7-dimension score radar (Time, Quality, Efficiency, Robustness, Process).

### 5.2 Firing an Attack at an Individual Desk
If a participant claims they did not receive an attack or requests a re-challenge:
1. In the **Active Kiosks & Desks** table, find their row.
2. Click the **⚡ Fire** button.
3. In the popup prompt, type the desired attack number (e.g. `2`) and press **Enter**.
4. The attack is dispatched specifically to that desk without affecting other participants.

---

## 6. Scoreboard Freeze & Final Ceremony (T+120m to T+150m)

### 6.1 Activating Scoreboard Freeze (T + 120m)
1. On the Admin Console top bar, click:
   ```
   ❄ Freeze Submissions
   ```
   *(Note: This toggles the freeze state. To freeze the public leaderboard while keeping participant submissions open, use the dedicated scoreboard endpoint below):*
   ```powershell
   Invoke-RestMethod -Uri "http://localhost:5080/admin/scoreboard/freeze" -Method Post -Headers @{"X-Admin-Key"="event_admin_secure_key_2026"}
   ```
2. The projector screen displays a pulsing `SCOREBOARD FROZEN` indicator.
3. Participants continue deploying patches, and their scores accumulate in the append-only SQLite ledger, but ranks remain hidden.

### 6.2 Event Conclusion (T + 150m)
1. At exactly T+150m, lock down submissions:
   * In Admin Console, click **❄ Freeze Submissions** (locks participant Deploy Patch button).
2. Broadcast conclusion message:
   ```powershell
   $body = @{ message = "COMPETITION COMPLETED. PLEASE STEP AWAY FROM KEYBOARDS." } | ConvertTo-Json
   Invoke-RestMethod -Uri "http://localhost:5080/admin/broadcast" -Method Post -Body $body -ContentType "application/json" -Headers @{"X-Admin-Key"="event_admin_secure_key_2026"}
   ```

### 6.3 Exporting Scores & Winner Audit
1. Export the official audit ledger to CSV:
   ```powershell
   Invoke-WebRequest -Uri "http://localhost:5080/admin/export/ledger" -Headers @{"X-Admin-Key"="event_admin_secure_key_2026"} -OutFile "official_final_scores.csv"
   ```
2. Unfreeze the public leaderboard on the projector:
   ```powershell
   Invoke-RestMethod -Uri "http://localhost:5080/admin/scoreboard/unfreeze" -Method Post -Headers @{"X-Admin-Key"="event_admin_secure_key_2026"}
   ```
3. Announce 1st, 2nd, and 3rd place winners.

---

## 7. Field Incident Troubleshooting

| Incident | Cause | Immediate Proctor Action |
| :--- | :--- | :--- |
| **Kiosk stuck on "Scanning..."** | UDP beacon blocked by router AP isolation | Press `Ctrl+Shift+S` on kiosk, enter `http://<HOST_IP>:5080` |
| **Participant corrupted files** | Syntax error in `routes/` or deleted file | Run `.\run.bat play -Reset` to restore baseline site code |
| **Kiosk shows "403 Forbidden"** | Token mismatch or duplicate registration | Press `Ctrl+Shift+S`, click **Clear Credentials & Re-Enroll** |
| **Evaluator times out on submissions** | Python engine on port 8000 stopped | Run `cd engine && python -m uvicorn breach_engine.api:app --port 8000` |
| **Host socket conflict on 5080** | Lingering ControlPlane process | Run `Get-NetTCPConnection -LocalPort 5080 \| % { Stop-Process -Id $_.OwningProcess -Force }` |

