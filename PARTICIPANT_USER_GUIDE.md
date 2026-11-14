# ROGUE AI: The Breach Protocol — Participant User Guide

**Target Audience:** Competition Participants, Defense Teams, and Evaluators  
**Application:** Rogue AI LabKiosk (Windows WPF Native Client)  
**Primary Objective:** Defend and remediate the AEGIS Municipal Defense Core application (`http://localhost:3000`) against active live-fire cyber breaches.

---

## 1. The Complete Participant Journey

```
[ Step 1: Onboarding ] ──> Enter Assigned Desk ID (e.g. DESK-07) and Callsign
           │
[ Step 2: Desk Enrolled ] ──> Receive Cryptographic Desk Token & Connect to SignalR
           │
[ Step 3: Standby Mode ] ──> AEGIS Website (:3000) Active in Purple Cyber UI
           │
[ Step 4: Breach Detected ] ──> Red Siren Alert & Incident Badge Appears on Kiosk
           │
[ Step 5: Incident Brief ] ──> Click Badge -> Read Symptoms, Telemetry & Scope
           │
[ Step 6: Code Remediation ] ──> Open Built-in Code Editor (C:\BreachWorkspace)
           │
[ Step 7: Terminal & AI CLI ] ──> Run Local Tests / Use OpenCode / AGY CLI Assistant
           │
[ Step 8: Deploy Patch ] ──> Click "Deploy Patch" Header Button
           │
[ Step 9: Evaluator Pipeline ] ──> Python Sandbox Validates Patch Across Test Suite
           │
[ Step 10: Score & Verdict ] ──> Instant Modal Verdict (+Points, Latency, Diagnostic)
```

---

## 2. Onboarding & Registration

### 2.1 First-Time Launch
1. Power on your station and open the **Rogue AI LabKiosk** application from the desktop shortcut.
2. The **Desk Onboarding Window** appears automatically.
3. The client will automatically listen on **UDP port 5082** for the organizer server beacon.
   * If auto-discovery succeeds, the `Server URL` field populates automatically with `http://<ORGANIZER_IP>:5080`.
   * **Manual Fallback:** If your screen displays *"Scanning for server..."*, press `Ctrl + Shift + S` to enter the IP address written on your desk card (e.g., `http://192.168.1.100:5080`).

### 2.2 Entering Credentials
* **Desk ID:** Enter your assigned station identifier (e.g., `DESK-01` through `DESK-50`).
* **Callsign / Name:** Enter your team or individual handle.
* Click **Join Breach Simulation**.

### 2.3 Cryptographic Enrollment & Storage
* The client contacts the server (`POST /api/kiosk/enroll`), obtains an encrypted `DeskToken`, and writes it to your profile.
* You do not need to log in again if you close the window; the kiosk remembers your identity across restarts.

---

## 3. The LabKiosk Interface Layout

The LabKiosk window is divided into four main functional areas:

```
┌──────────────────────────────────────────────────────────────────────────────────┐
│  [STATUS BAR] Desk: DESK-07 | Status: Nominal | 👤 ShadowCore | 🛡️ Active Badges │
├─────────────────────────┬────────────────────────────┬───────────────────────────┤
│                         │                            │                           │
│   AEGIS WEB PORTAL      │      CODE EDITOR           │     TERMINAL & AI CLI     │
│   (Embedded Edge        │   (AvalonEdit / Syntax     │   (xterm.js Console /     │
│    WebView2 Browser)    │    Highlighted Editor)     │    PowerShell / AI)       │
│                         │                            │                           │
│   http://127.0.0.1:3000 │  • routes/traffic.js       │   $ npm test              │
│   • 3D Smart City       │  • routes/vault.js         │   $ opencode "Fix SQLi"   │
│   • TrafficGrid         │  • lib/auth.js             │                           │
│   • PowerCore           │                            │                           │
│   • CitizenVault        │                            │                           │
│                         │                            │                           │
└─────────────────────────┴────────────────────────────┴───────────────────────────┘
```

### 3.1 Top Action & Status Bar
* **Status Message:** Displays real-time operational messages (e.g., `"Connected. All systems secure. No active attacks."` or `"Active breach detected: #1. Fix the code, then submit."`).
* **Profile Button (`👤 <Name>`):** Displays your callsign. Clicking it shows your Desk ID and Station IP.
* **Restart Site Button:** If your local website crashes due to a syntax error or frozen loop, click **Restart Site** to gracefully terminate and reboot Node.js on port 3000.
* **Deploy Patch Button (`⚡ Deploy Patch`):** The primary competition action button. Submits your current workspace files to the organizer evaluation engine for automated grading.
* **Telemetry & Quota Counters:**
  * **AI Prompts Remaining:** Tracks your remaining AI assistant quota.
  * **Local Penalties:** Displays accumulated local test suite failure penalties (-3.0 pts deduction per local test failure).

---

## 4. Breach Detection & Incident Management

### 4.1 When an Attack Lands
1. When an adversary launches a breach, the platform triggers an alert:
   * A flashing red indicator banner appears across the top status bar.
   * A red glowing badge appears in the **Active Incidents** panel (e.g., `⚡ #1: Portal Integrity Alert ⏱ 01:24`).
   * The timer shows elapsed time since the breach landed.
2. The adversary patches specific files in your workspace (`C:\BreachWorkspace`) to introduce vulnerability or malfunction.

### 4.2 The Incident Briefing Window
* Click directly on any red incident badge to open the **Incident Briefing Window**:
  * **Title & Code:** E.g., `Incident #1: Portal Integrity Alert (Reflected XSS)`.
  * **Classification:** CWE / OWASP classification (e.g., `CWE-79: Cross-Site Scripting`).
  * **Symptoms:** What is broken on your website (e.g., `"Unauthorized script execution detected on the search telemetry parameter"`).
  * **Telemetry Clues:** Server log excerpts and stack trace indicators to help locate the bug.

---

## 5. Remediation, Workspace & AI Assistant

### 5.1 Local Workspace Structure
All code for the AEGIS defense core resides in:
```
C:\BreachWorkspace
├── lib/
│   ├── auth.js          (Session cookies, token validation, role checks)
│   ├── cache.js         (In-memory caching and sync layers)
│   ├── db.js            (SQLite database connections)
│   └── html.js          (Template layout and escaping helpers)
├── routes/
│   ├── overview.js      (Main operational dashboard & city metrics)
│   ├── traffic.js       (TrafficGrid routing and signals)
│   ├── power.js         (PowerCore distribution zones)
│   ├── vault.js         (Citizen records and clearance gates)
│   └── security.js      (Security Center DEFCON telemetry)
├── public/
│   ├── aegis.css        (Purple-themed cyber-defense stylesheet)
│   └── app.js           (3D Canvas city engine and telemetry widgets)
├── server.js            (Application bootstrap & port 3000 listener)
└── package.json
```

### 5.2 Editing Code
* You can edit code directly in the embedded **Code Editor** tab inside LabKiosk, or open `C:\BreachWorkspace` in VS Code / your favorite local editor.
* Changes saved to disk are immediately picked up by the local Node.js server.

### 5.3 Using the AI Assistant CLI
* In the embedded terminal window, you have access to local coding assistants:
  ```bash
  # Using OpenCode CLI:
  opencode "Examine routes/traffic.js and fix the input validation vulnerability"
  ```
* **Quota Management:** Every prompt consumed by the AI CLI is tracked in the header. Use concise, targeted prompts to avoid exhausting your AI quota.

### 5.4 Anti-AI Leverage Safeguard
* To maintain competition integrity, the platform automatically normalizes file timestamps and isolates Git metadata when attacks land. AI tools cannot simply ask Git *"what changed in the last commit"*; participants must read and understand the exploit symptoms.

---

## 6. Patch Submission & Evaluation Workflow

### 6.1 Submitting Your Fix
1. Verify locally that your website works:
   * Refresh the embedded browser (`http://localhost:3000`).
   * Test your endpoint or form.
2. Click **Deploy Patch** on the LabKiosk top bar.
3. The kiosk takes a cryptographic snapshot of your workspace and uploads it securely to the Control Plane.
4. The Control Plane routes your code into the isolated **Python Breach Sandbox** on port 8000.

### 6.2 The Evaluation Sandbox
* The evaluator runs the official attack test harness (`check.py`):
  * **Security Verification:** Proves that the exploit vector is neutralized.
  * **Functional Regression Test:** Verifies that normal user operations on that route still return expected HTTP 200 data.
  * **Sandbox Limits:** The sandbox enforces strict limits (512 MB RAM, 64 processes, 15s timeout). Submissions that attempt fork bombs or memory exhaustion fail closed.

### 6.3 The Verdict Modal
Within 1–3 seconds, a modal dialog appears on your kiosk:
* **PASSED (Green):**
  * Displays points awarded (e.g., `+100.0 pts`).
  * Shows speed bonus multiplier and solve duration.
  * The attack badge is removed from your screen and added to your solved count.
* **FAILED (Red):**
  * Displays diagnostic feedback explaining why the test failed (e.g., `FAILED: Exploit payload '<script>' still executes on search input`).
  * Zero points awarded. The attack remains active.
  * Analyze the reason, modify your code, and submit again.

---

## 7. Reliability, Disconnections & Edge Cases

| Scenario | What Happens | What You Should Do |
| :--- | :--- | :--- |
| **Accidental Kiosk Close / Crash** | Workspace files remain untouched on disk (`C:\BreachWorkspace`) | Simply relaunch **LabKiosk**; it reconnects and restores state within 2 seconds |
| **Venue Wi-Fi Disconnect** | Top indicator shows yellow `Reconnecting...` | Kiosk automatically reconnects with backoff; no action needed |
| **Node.js Website Crashes** | Browser displays `127.0.0.1 refused to connect` | Click **Restart Site** in kiosk toolbar |
| **Completely Corrupted Workspace** | Participant accidentally deleted essential files | Run `.\run.bat play -Reset` to restore clean baseline files without losing score |
| **Submission Timeout / Server Busy** | Client displays `Evaluator timeout` | Wait 10 seconds and click **Deploy Patch** again |
| **Scoreboard Freeze (T-30m)** | Live leaderboard freezes on large screen | Keep submitting! Your patches are still evaluated and points recorded |

