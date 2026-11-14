# ROGUE AI: The Breach Protocol — Administrator Control Guide & Architecture Reference

**Audience:** Event Technical Directors, Stage Operations Lead, and Systems Administrators  
**Control Plane URL:** `http://<HOST_IP>:5080/admin/`  
**API Port:** 5080 (REST + SignalR Hub at `/hub`)  
**Backend DB:** `server/ControlPlane/Data/breach.db` (SQLite WAL mode)

---

## 1. Executive Architecture of the Admin Plane

The ROGUE AI Admin Plane operates through a single-page management dashboard backed by ASP.NET Core endpoints, a real-time SignalR duplex hub, and the Python Breach Engine.

```
┌────────────────────────────────────────────────────────────────────────┐
│                        ADMIN WEB DASHBOARD                             │
│                  (http://localhost:5080/admin/)                        │
│                                                                        │
│  [Top Header]       Timer, UTC Clock, Admin User, Freeze Button        │
│  [Section 1]        Attack Dispatch State Machine Table                │
│  [Section 2]        Participant Evaluation Scorecard & Pass/Fail Matrix │
│  [Section 3 - Left] Active Kiosks & Desks Table (with Direct Fire)     │
│  [Section 3 - Right]Live SignalR Arena Event Log Console               │
└──────────────┬───────────────────────────────┬─────────────────────────┘
               │ HTTP REST / Cookies           │ WebSocket / SignalR
               ▼                               ▼
┌────────────────────────────────────────────────────────────────────────┐
│                    ASP.NET CORE CONTROL PLANE (:5080)                  │
│                                                                        │
│  • Program.cs                  : 32 Admin REST Endpoints               │
│  • BreachHub.cs                : Duplex Real-Time Event Hub            │
│  • AttackDispatcher.cs         : File Patching, Delivery, Reset Engine │
│  • RogueAgentOrchestrator.cs   : Attack State & Manifest Registry      │
│  • Database.cs                 : SQLite Append-Only Ledger & Triggers  │
└──────────────┬───────────────────────────────┬─────────────────────────┘
               │ IPC / HTTP (:8000)            │ Filesystem (:3000 / Sync)
               ▼                               ▼
       Python Breach Engine           Participant Kiosks (1..50)
     (FastAPI Evaluator Sandbox)     (LabKiosk WPF + C:\BreachWorkspace)
```

---

## 2. Root-Cause Analysis: The 4 Persistent Attacks on Participant Screens

### 2.1 The Issue
During pre-event testing, participants' screens (LabKiosk header) continue to display four active attack badges (e.g. `Incident #0`, `Incident #2`, `Incident #3`, `Incident #7`) even after closing and restarting the kiosk.

### 2.2 Forensic Cause in Implementation
This issue is caused by **persistent state rows in the SQLite database (`server/ControlPlane/Data/breach.db`)**:

1. In `server/ControlPlane/Data/Database.cs`, line 38, the platform creates table `attack_deliveries`:
   ```sql
   CREATE TABLE IF NOT EXISTS attack_deliveries (
     attack_id INTEGER NOT NULL,
     participant_id TEXT NOT NULL,
     landed_at TEXT,
     solved_at TEXT,
     PRIMARY KEY (attack_id, participant_id)
   );
   ```
2. When test attacks are fired (e.g., during development or dry-runs), `AttackDispatcher.cs` inserts a row:
   ```sql
   INSERT OR REPLACE INTO attack_deliveries (attack_id, participant_id, landed_at) VALUES ($a, $p, $t);
   ```
3. When any participant launches LabKiosk, `MainWindow.xaml.cs` line 444 calls:
   ```csharp
   var status = await _api.GetStatusAsync(_pid);
   ```
4. On the Control Plane, `Program.cs` line 321 handles `GET /api/kiosk/{id}/status`:
   ```csharp
   var active = db.Query<int>(
       "SELECT attack_id FROM attack_deliveries WHERE participant_id = @p AND landed_at IS NOT NULL AND solved_at IS NULL ORDER BY attack_id",
       ("@p", id));
   return Results.Ok(new { status, activeAttacks = active, paused = s.Paused });
   ```
5. Inspection of `breach.db` verified the exact 4 delivered attacks for test participant `p75310da7de` ('Shukla2410'):
   ```python
   [('p75310da7de', 0, '2026-11-12T13:47:01.0762709Z', None),
    ('p75310da7de', 3, '2026-11-12T13:58:59.2635689Z', None),
    ('p75310da7de', 2, '2026-11-12T13:59:01.6529801Z', None),
    ('p75310da7de', 7, '2026-11-13T15:38:35.7527388Z', None)]
   ```
6. Because `solved_at` is `NULL`, every time the kiosk starts or reconnects, the server returns these 4 attacks, and the kiosk adds them to `_activeAttacks` and renders badges on the UI.

### 2.3 How an Administrator Properly Resets Attack State

#### Option A: Reset All Attacks Across All Participants (Preserving Participant Registrations & Scores)
Use the Control Plane backend endpoint `POST /api/admin/attacks/reset-all`:
* **PowerShell Command:**
  ```powershell
  Invoke-RestMethod -Uri "http://localhost:5080/api/admin/attacks/reset-all" -Method Post -Headers @{"X-Admin-Key"="event_admin_secure_key_2026"}
  ```
* **What Happens:**
  1. Deletes delivery rows: `DELETE FROM attack_deliveries WHERE attack_id = $a`.
  2. Restores clean source files for those attacks in all participant workspaces (`WorkspaceStore.RestoreFiles`).
  3. Resets attack dispatch status to `scheduled` / `Pending`.
  4. Broadcasts SignalR event `ATTACK_RESET` to all connected kiosks.
  5. The kiosks clear their `_activeAttacks` and remove the badges instantly.
  6. Participant accounts, PINs, and score history are **retained**.

#### Option B: Reset Attack State for One Specific Participant
* **PowerShell Command:**
  ```powershell
  # Reset attack 0 for participant p75310da7de
  Invoke-RestMethod -Uri "http://localhost:5080/api/admin/attacks/0/reset?target=p75310da7de" -Method Post -Headers @{"X-Admin-Key"="event_admin_secure_key_2026"}
  ```

#### Option C: Complete Fresh Event Reset (Pre-Kickoff Clean Slate)
* **PowerShell Command:**
  ```powershell
  Invoke-RestMethod -Uri "http://localhost:5080/api/admin/reset-session" -Method Post -Headers @{"X-Admin-Key"="event_admin_secure_key_2026"}
  ```
  *(Clears all test participants, test submissions, test deliveries, and resets the score ledger.)*

---

## 3. Detailed Breakdown of Every Admin Control & Operation

### SECTION 1: HEADER & GLOBAL CONTROLS

#### 1.1 Freeze / Unfreeze Submissions (`#freeze-btn`)
* **Location:** Top-right of the Attack Dispatcher card.
* **UI Function:** `toggleFreeze()` in `admin.js`.
* **Backend Endpoint:** `POST /api/admin/event/freeze` and `POST /api/admin/event/unfreeze`.
* **What it does:** Toggles global submission state (`EventState.Frozen`).
* **When to use:**
  - T-30 minutes before event conclusion (scoreboard freeze).
  - Emergency pause during dispute or technical investigation.
* **System Impact:**
  - When frozen, any participant clicking **Deploy Patch** receives `Results.Conflict("Submissions are currently frozen by organizers")`.
  - SignalR broadcasts `FREEZE_STATE_CHANGED` to all kiosks.
* **Reversible:** Yes (click again to Unfreeze).
* **Verification:** Button changes to green `▶ Unfreeze Submissions`. Kiosks disable patch deploy button.

#### 1.2 Fire Boss Attack (`#fire-boss-btn`)
* **Location:** Top-right header next to Freeze.
* **UI Function:** `fireBossAttack()` in `admin.js`.
* **Backend Endpoint:** `POST /api/admin/attacks/11/fire-arena`.
* **What it does:** Dispatches Attack #11 (Band 5: Session Fracture) to all connected kiosks simultaneously.
* **When to use:** During Phase 4 finale (Wave 4) or final 20 minutes of competition.
* **System Impact:**
  - Patches `site/lib/auth.js` and `site/routes/overview.js` in all participant workspaces.
  - Broadcasts red siren alert to all kiosks.
* **Reversible:** Yes, via `POST /api/admin/attacks/11/reset`.
* **Verification:** Red alert banner appears on kiosks; attack row #11 state changes to `ArenaLanded`.

#### 1.3 Event Elapsed Timer (`#event-timer`)
* **Location:** Top header center.
* **What it does:** Displays elapsed competition time (`HH:MM:SS`) persisted in browser `localStorage`.
* **Verification:** Advances every second.

---

### SECTION 2: ATTACK DISPATCH STATE MACHINE TABLE

Displays the inventory of attacks (up to 33 attacks) with 8 columns:
* `#` (ID)
* `Slot` (Onboarding/Canary, Reserve, Slot 1..N)
* `Attack Title`
* `Codename`
* `Band` (Difficulty 1 to 5)
* `Mode` (`manual` or `scheduled`)
* `Schedule`
* `State` (`Pending`, `CanaryFired`, `CanaryConfirmed`, `ArenaImminent`, `ArenaLanded`)
* `Actions` (3 action buttons per attack)

#### 2.1 Action: "Canary" (`fireCanary(id)`)
* **Endpoint:** `POST /api/admin/attacks/{id}/canary`.
* **What it does:** Dispatches the selected attack **only to the designated Canary desk** (configured via `RogueAgent:CanaryDeskId` in `appsettings.json`, e.g., `CANARY` or proctor station).
* **When to use:** Before deploying an attack wave to the 50 participants, verify that the attack patch lands cleanly and is solvable on the proctor desk.
* **State Change:** Moves attack state from `Pending` to `CanaryFired`.
* **Verification:** Attack row turns yellow (`CanaryFired`). Proctor kiosk receives attack payload.

#### 2.2 Action: "Confirm" (`confirmCanary(id)`)
* **Endpoint:** `POST /api/admin/attacks/{id}/confirm-canary`.
* **What it does:** Marks the canary dry-run as verified and ready for arena deployment.
* **When to use:** After the proctor confirms the canary desk patch works as expected.
* **State Change:** Moves attack state from `CanaryFired` to `CanaryConfirmed`. Enables the **Fire Arena** button.

#### 2.3 Action: "Fire Arena" (`fireArena(id)`)
* **Endpoint:** `POST /api/admin/attacks/{id}/fire-arena`.
* **What it does:** Confirms and broadcasts the attack to **all participant desks in the arena**.
* **When to use:** To start an attack wave according to the event schedule.
* **State Change:**
  - Triggers 10-second countdown alert on kiosks (`ArenaImminent`).
  - After countdown, inserts rows in `attack_deliveries`, copies attack files into participant workspaces (`WorkspaceStore`), and marks `ArenaLanded`.
* **Verification:** Kiosks sound alarm and show new incident badge. Table state changes to `ArenaLanded`.

---

### SECTION 3: PARTICIPANT EVALUATION SCORECARD & DIAGNOSTICS

Displays real-time performance and evaluation metrics for every desk.

#### 3.1 Scorecard Columns
* **Desk:** Assigned desk ID (e.g. `DESK-01`).
* **Participant / Team:** Callsign and name.
* **Station IP:** Remote IP of the participant machine.
* **PIN / Pass:** Cryptographic desk enrollment password.
* **Score:** Current cumulative score (e.g., `1245.5 pts`).
* **Pass / Solved:** Number of successfully resolved attacks.
* **Local Penalties:** Local failed test runs (-3.0 pts deduction per local test suite failure).
* **Attack Columns (#0 to #8):** Real-time pass/fail indicators:
  - `--` (Grey): Not yet dispatched.
  - `RUN` (Blue): Dispatched / Active breach in progress.
  - `PASS` (Green): Solved and verified.
  - `FAIL` (Red): Fix submitted but failed acceptance checks. Hovering shows exact failure reason.
* **Gates (Out/Comp):**
  - `OUTCOME`: Hard gate requiring ≥50% success on attempted attacks.
  - `COMP`: Compliance gate verifying zero sandbox escapes and zero tampering.
* **Diagnostic Verdict / Reason:** Detailed explanation of the last evaluation verdict (e.g., `Failed: Test case 2 expected HTTP 200 but received 404`).

#### 3.2 Action: "Audit" (`openEvalModal(pid)`)
* **What it does:** Opens the **7-Dimension Evaluation Framework Modal** for the selected participant.
* **What it displays:**
  1. Outcome Gate status.
  2. Compliance Gate status.
  3. The 7 Scoring Dimensions:
     - Time (30% weight): Compute and solve speed.
     - Quality (25% weight): Code cleanliness, CC, and MI index.
     - Efficiency (20% weight): AI prompts and token budget consumed.
     - Robustness (15% weight): Edge case and variant tests passed.
     - Process (10% weight): Development cadence and test hygiene.
  4. Itemized list of all attacks with exact diagnostic logs.
* **Verification:** Click **Audit** on any row; modal appears instantly with full diagnostic history.

---

### SECTION 4: ACTIVE KIOSKS & CONSOLE LOG

#### 4.1 Active Kiosks Table
* Displays real-time heartbeat and connection status for every connected LabKiosk.
* **Active Breach & Elapsed:** Shows the active incident ID with a live timer showing how long the participant has been working on it (e.g. `⚡ #2: Stored XSS ⏱ 04:12`).
* **Git & AI Status:** Shows participant's git branch (`participant/DESK-XX`), whether AGY CLI quota is active, and total OpenCode prompts used.

#### 4.2 Action: "⚡ Fire" Button (`promptFireOnKiosk(kioskId, name)`)
* **Endpoint:** `POST /admin/attacks/{n}/fire?targetId={kioskId}`.
* **What it does:** Prompts the admin to enter an attack number (e.g., `1`), and fires that attack **specifically to that individual desk**.
* **When to use:**
  - Individual participant re-test.
  - Demonstration or penalty testing.
  - Asymmetric handicap challenge.

#### 4.3 Event Console Log
* Real-time WebSocket terminal displaying all arena events:
  - Participant logins and registrations.
  - Attack dispatches and landings.
  - Code submissions and evaluation verdicts.
  - SignalR connection dropouts and reconnects.
* **Clear Button:** Clears the visible log container.

---

## 4. Participant Management Operations

### 4.1 How to View the Complete List of Registered Participants
* **In UI:** Navigate to `http://localhost:5080/admin/`. The **Active Kiosks & Desks** card displays every enrolled participant.
* **Via API:**
  ```bash
  curl -s http://localhost:5080/admin/kiosks -H "X-Admin-Key: <ADMIN_KEY>"
  ```

### 4.2 How to Search for a Particular Participant
* **Current UI Status:** There is currently no dedicated search input filter on the Admin UI table.
* **Workaround:** Use browser native search (`Ctrl + F`) and type the desk ID (e.g. `DESK-12`) or participant callsign.
* **Recommendation:** Add a client-side search input filter above `#kiosk-list` and `#eval-list` (see Section 6).

### 4.3 How to Inspect an Individual Participant's Details
* **In UI:** In the **Participant Evaluation Scorecard** table, click the **Audit** button in the row for that participant.
* **Via API:**
  ```bash
  curl -s http://localhost:5080/admin/kiosks/<PARTICIPANT_ID> -H "X-Admin-Key: <ADMIN_KEY>"
  ```
  Returns full JSON containing all submission attempts, scores, and telemetry events.

### 4.4 How to Disqualify or Remove a Participant
* **Backend Endpoint:** `POST /admin/kiosks/{id}/disqualify`.
* **What it does:**
  1. Sets participant status to `disqualified` in SQLite database.
  2. Sends `ELIMINATED` event over SignalR to the participant's kiosk.
  3. The kiosk immediately locks down with a red disqualification screen and stops taking input.
* **Impact on Records:** The participant's workspace, scores, submissions, and telemetry remain intact in the database for post-event audit. They are simply prevented from continuing.
* **Execution Command (PowerShell):**
  ```powershell
  Invoke-RestMethod -Uri "http://localhost:5080/admin/kiosks/<PARTICIPANT_ID>/disqualify" -Method Post -Headers @{"X-Admin-Key"="event_admin_secure_key_2026"}
  ```

---

## 5. Summary of Supported vs Missing Admin Controls

| Operation | Supported via UI? | Supported via API? | Method to Execute |
| :--- | :---: | :---: | :--- |
| **View Kiosks & Heartbeats** | **YES** | **YES** | Active Kiosks Table |
| **View 7-Dimension Scorecards** | **YES** | **YES** | Participant Scorecard -> Audit Modal |
| **Canary Dispatch & Confirm** | **YES** | **YES** | Canary / Confirm buttons |
| **Fire Attack to Arena** | **YES** | **YES** | Fire Arena button |
| **Fire Attack to Specific Desk** | **YES** | **YES** | `⚡ Fire` button in Kiosk row |
| **Freeze / Unfreeze Submissions** | **YES** | **YES** | `❄ Freeze Submissions` button |
| **Fire Boss Attack** | **YES** | **YES** | `🔥 Fire Boss Attack` button |
| **Reset Attack State for All** | *API Only* | **YES** | `POST /api/admin/attacks/reset-all` |
| **Reset Attack for One Desk** | *API Only* | **YES** | `POST /api/admin/attacks/{id}/reset?target={id}` |
| **Disqualify Participant** | *API Only* | **YES** | `POST /admin/kiosks/{id}/disqualify` |
| **Broadcast Announcement** | *API Only* | **YES** | `POST /admin/broadcast` |
| **Export Score Ledger CSV** | *API Only* | **YES** | `GET /admin/export/ledger` |
| **Filter / Search Desks** | *Browser Ctrl+F* | **YES** | Native browser search |

---

## 6. Recommendations for Admin UI Enhancements

To make event operations seamless without needing command-line tools during the event, the following controls are recommended for addition directly to `server/ControlPlane/wwwroot/admin/index.html`:

1. **"Reset All Attacks" Button in Dispatcher Card Header:**
   Calls `POST /api/admin/attacks/reset-all` after confirmation prompt.
2. **"Disqualify / Eject" Button in Kiosks Table:**
   Adds an action button in each kiosk row calling `POST /admin/kiosks/{id}/disqualify`.
3. **Instant Desk Search Filter Input:**
   A simple text box that dynamically filters both the Kiosks table and Scorecard table as the operator types.
4. **"Broadcast Message" Input Bar:**
   A modal or input box calling `POST /admin/broadcast` to send custom alerts to all kiosks.

