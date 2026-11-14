# ROGUE AI: The Breach Protocol — Field Troubleshooting Guide

**Scope:** Rapid Triage & Incident Resolution during Live Event Operations  
**Audience:** Technical Directors, Lab Proctors, and System Administrators  
**Status:** Certified Stage 6 Production Deliverable

---

## 1. Quick Triage Decision Matrix

| Symptom | Probable Cause | Quick Action | Time to Recover |
| :--- | :--- | :--- | :--- |
| **Kiosk stuck on "Searching for Server..."** | UDP 5082 broadcast blocked or AP Isolation active | Press `Ctrl+Shift+S` on kiosk, enter `http://<HOST_IP>:5080` manually | 30 seconds |
| **Kiosk shows "ERR_CONNECTION_REFUSED" on `:3000`** | Local Node.js crashed or port 3000 blocked | Click **Restart Site** in kiosk toolbar or kill orphan node processes | 15 seconds |
| **HTTP 403 Forbidden on Snapshot / Fix Submission** | Desk token invalidated due to host restart or re-enrollment | Clear local kiosk token via `Ctrl+Shift+S` and re-enter Desk ID | 45 seconds |
| **SignalR status dot turns RED on kiosk** | Wi-Fi hiccup or transient network disconnect | Automatic backoff reconnects in 2–10s; if hung, restart LabKiosk | 10 seconds |
| **Submission evaluation hangs or returns 502** | Python Breach Engine process stopped on port 8000 | Restart Python engine via `cd engine && python -m uvicorn ...` | 20 seconds |
| **Host fails to bind port 5080 on startup** | Orphan ControlPlane process holding socket | Run PowerShell port release command: `Stop-Process` | 10 seconds |
| **Corrupted workspace / participant broke Node modules** | Participant deleted key files in `C:\BreachWorkspace` | Run `.\run.bat play -Reset` to restore pristine baseline site | 30 seconds |

---

## 2. Detailed Diagnostic & Resolution Procedures

### Issue 1: Kiosk Auto-Discovery Failure
**Diagnostic:** Kiosk status displays "Scanning for Rogue AI Server on UDP 5082..." indefinitely.
* **Root Cause:** Venue Wi-Fi router has "Wireless Client Isolation" or "AP Isolation" enabled, which drops UDP broadcast packets between wireless clients.
* **Resolution Steps:**
  1. On the affected kiosk, press keyboard shortcut:
     ```
     Ctrl + Shift + S
     ```
  2. The **Manual Server Override** modal appears.
  3. Enter the exact IP and port of the Organizer Host:
     ```
     http://192.168.1.100:5080
     ```
  4. Click **Connect**.
  5. The kiosk bypasses UDP discovery and connects directly via TCP HTTP/SignalR.

---

### Issue 2: Local AEGIS Website Failure (`ERR_CONNECTION_REFUSED`)
**Diagnostic:** The embedded browser inside LabKiosk displays `127.0.0.1 refused to connect` or blank screen.
* **Root Cause:**
  1. The participant entered invalid JavaScript syntax in `server.js`, causing the Node.js process to crash on launch.
  2. An orphan `node.exe` process is already holding port 3000.
* **Resolution Steps:**
  1. **Quick Reset via UI:** Click the **Restart Site** button on the LabKiosk top action bar.
  2. **Manual Process Cleanup:** Open PowerShell on the participant machine and run:
     ```powershell
     Stop-Process -Name node -Force -ErrorAction SilentlyContinue
     ```
     Then click **Restart Site** in LabKiosk.
  3. **Inspect Crash Log:** Check terminal output or inspect `C:\BreachWorkspace\site\node_error.log` for participant syntax errors.
  4. **Emergency Workspace Recovery:** If participant corrupted core framework files:
     ```cmd
     .\run.bat play -Reset
     ```
     This cleans `C:\BreachWorkspace` and extracts fresh source code without losing score points.

---

### Issue 3: Desk Token Mismatch (HTTP 403 Forbidden)
**Diagnostic:** Participant attempts to deploy a patch or save a snapshot, but receives `403 Forbidden: Invalid Desk Token`.
* **Root Cause:** The Organizer Host database was reset, or a second machine registered under the identical `DESK-XX` identifier, generating a newer cryptographic token.
* **Resolution Steps:**
  1. Check Admin Console `http://localhost:5080/admin/` to verify the active `participantId`.
  2. If the desk was re-assigned:
     * In LabKiosk, press `Ctrl + Shift + S`.
     * Click **Clear Stored Credentials & Re-Enroll**.
     * Enter the assigned `DESK-XX` ID and Callsign.
     * Click **Connect** to obtain a fresh valid token.
  3. Alternatively, delete the stored token file on the kiosk:
     ```powershell
     Remove-Item "$env:LOCALAPPDATA\RogueAI\desk_token.dat" -Force -ErrorAction SilentlyContinue
     ```
     Relaunch LabKiosk.

---

### Issue 4: Python Breach Engine Failure (Port 8000)
**Diagnostic:** Submissions time out; Admin console shows Breach Engine offline or red indicator.
* **Root Cause:** The Python FastAPI process encountered an unhandled exception or was closed accidentally.
* **Resolution Steps:**
  1. Open a new Command Prompt on the Organizer Host:
     ```cmd
     cd C:\RogueAI\engine
     python -m uvicorn breach_engine.api:app --host 127.0.0.1 --port 8000
     ```
  2. Verify engine is responding:
     ```powershell
     Invoke-RestMethod -Uri "http://127.0.0.1:8000/docs"
     ```
  3. In Control Plane, pending queued submissions will resume evaluation automatically.

---

### Issue 5: Port 5080 Conflict on Organizer Host Startup
**Diagnostic:** Running `.\run.bat host` fails with `System.IO.IOException: Failed to bind to address http://0.0.0.0:5080: address already in use`.
* **Root Cause:** A previous session of `ControlPlane.exe` or `dotnet.exe` remained running in the background.
* **Resolution Steps:**
  1. Run the following one-line PowerShell command to kill the lingering process:
     ```powershell
     Get-NetTCPConnection -LocalPort 5080 -ErrorAction SilentlyContinue | ForEach-Object { Stop-Process -Id $_.OwningProcess -Force }
     ```
  2. Also clear port 8000 if needed:
     ```powershell
     Get-NetTCPConnection -LocalPort 8000 -ErrorAction SilentlyContinue | ForEach-Object { Stop-Process -Id $_.OwningProcess -Force }
     ```
  3. Re-run `.\run.bat host`.

---

### Issue 6: Master Database Restore & Recovery
**Diagnostic:** Host suffered ungraceful power loss or hardware crash; database is locked or corrupted.
* **Automatic Safeguard:** The Control Plane automatically generates timestamped SQLite snapshots every 60 minutes in `server/ControlPlane/data/backups/`.
* **Restoration Procedure:**
  1. Stop all host processes (`Ctrl+C` or kill dotnet process).
  2. Navigate to the database directory:
     ```powershell
     cd C:\RogueAI\server\ControlPlane\data
     ```
  3. Delete any stale WAL and lock files:
     ```powershell
     Remove-Item "controlplane.db-wal", "controlplane.db-shm" -Force -ErrorAction SilentlyContinue
     ```
  4. Locate the most recent valid backup in `backups/`:
     ```powershell
     Get-ChildItem backups | Sort-Object LastWriteTime -Descending | Select-Object -First 5
     ```
  5. Copy the latest backup over the active database file:
     ```powershell
     Copy-Item "backups\controlplane_backup_<LATEST>.db" "controlplane.db" -Force
     ```
  6. Verify database integrity using SQLite CLI or PowerShell:
     ```powershell
     python -c "import sqlite3; con = sqlite3.connect('controlplane.db'); print(con.execute('PRAGMA integrity_check;').fetchall())"
     ```
     *Output must read: `[('ok',)]`*
  7. Restart host via `.\run.bat host`. All participant records and scores up to the backup timestamp are restored.

---

## 3. Emergency Proctor Hotlines & Shortcuts

| Action | Shortcut / Command | Target Machine |
| :--- | :--- | :--- |
| **Kiosk Config & Manual IP** | `Ctrl + Shift + S` | Participant Kiosk |
| **Force Restart AEGIS Site** | Top Toolbar "Restart Site" | Participant Kiosk |
| **Open DevTools in Kiosk** | `F12` (in unconstrained mode) | Participant Kiosk |
| **Emergency Global Pause** | Admin Dashboard -> "Emergency Freeze" | Organizer Host |
| **Manual Submission Flush** | Admin Dashboard -> "Retry Evaluator Queue" | Organizer Host |
| **Export Official Score CSV** | `http://localhost:5080/api/admin/export/ledger` | Organizer Host |

