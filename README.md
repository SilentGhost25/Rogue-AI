# ROGUE AI — The Breach Protocol (Production Build)

**Live-Attack Cybersecurity Defense Competition Platform**  
Event Date: 13 November 2026

---

## 1. Architecture Overview

`Rogue AI: The Breach Protocol` is a single unified application platform designed for high-stakes cybersecurity defense events on campus LANs:

- **Participant Defense Studio (`LabKiosk.exe`)**:
  - **Left Pane**: Live Website (`Harbour Notes`) running in Microsoft WebView2 with instant live reload on attacks or code fixes.
  - **Center Pane**: Built-in In-App Code Viewer & Editor (AvalonEdit) with full workspace tree explorer, syntax highlighting, and FileSystemWatcher live synchronization.
  - **Right Pane**: Embedded ConPTY native terminal running authentic `agy` (Antigravity CLI) and `opencode` with real interactive vibe coding, file editing, and automated quota monitoring.
  - **Status Strip**: Live celebration badges (`SOLVED` / `FAILED`), layout switchers (`All 3`, `Web+Term`, `Code+Term`), real-time status banners, and one-click Organizer Admin Console access.
- **Organizer Command Center (`AdminWindow` / `http://127.0.0.1:5080/index.html`)**:
  - 50-node live participant grid with telemetry, solve counts, and AI quota status.
  - Clickable participant scorecard modal with full append-only score ledger and GitHub branch diff link (`participant/<id>`).
  - Intrusion dispatch deck for 7 scheduled attacks (broadcast to all nodes or targeted test landing).
  - Global event pause, resume, emergency broadcast, and scoreboard freeze.
  - Tamper-evident SQLite score ledger CSV export.
- **Stadium Projector View (`http://127.0.0.1:5080/projector.html`)**:
  - High-contrast cyberpunk stadium leaderboard for auditorium projectors.
  - Live incident feed ticker with Web Audio API synthesizer fanfare on solves.
  - Attack status radar and final-hour score masking support.
- **Evaluation Engine (`engine/breach_engine`)**:
  - Ephemeral child Node.js runners on free ports.
  - Deterministic multi-tier checks (`regression_check`, `replay_check`, `variant_check`).
  - Tamper-proof append-only SQLite score ledger protected by SQLite DB triggers.

---

## 2. Attack Packs Summary

| # | Attack Name | Corruption Mechanism | Verification Checks |
|---|---|---|---|
| **1** | **Defacement & Route Redirect** | `corrupted/public/style.css` banner + `corrupted/lib/html.js` redirect hook | Checks home page loads without defacement CSS or unapproved redirects. |
| **2** | **Stored Cross-Site Scripting (XSS)** | `corrupted/routes/posts.js` and `search.js` unescaped comments | Checks script tags are properly HTML-escaped. |
| **3** | **SQL Injection Extraction** | `corrupted/routes/search.js` raw string concatenation in SQL query | Injects `' UNION SELECT` payload and verifies query sanitization. |
| **4** | **Hardcoded Admin Backdoor** | `corrupted/routes/api.js` secret master bypass key | Verifies master backdoor key is rejected while valid auth is preserved. |
| **5** | **Auth Bypass & Permissive CORS** | `corrupted/app.js` wildcard `*` CORS and missing auth middleware | Checks unauthorized requests are rejected with 401/403. |
| **6** | **CPU Performance Sabotage** | `corrupted/routes/api.js` 200,000 iterative SHA256 blocking loop | Benchmarks response latency under load (<200ms expected). |
| **7** | **Poisoned Dependency Hook** | `corrupted/server.js` and `package.json` rogue dependency injection | Inspects dependency tree and confirms rogue package is excised. |

---

## 3. How to Run

### Development & Testing
```powershell
# 1. Run site tests
cd site ; npm test

# 2. Run engine tests
cd engine ; python -m unittest discover -s tests -t .

# 3. Run control plane unit tests
dotnet test server/ControlPlane.Tests/ControlPlane.Tests.csproj

# 4. Start Python Engine & Control Plane
cd engine ; python -m breach_engine.api
cd server/ControlPlane ; dotnet run --urls http://127.0.0.1:5080

# 5. Run 50-Kiosk Concurrency Stress Test
python sim/fake_kiosks.py --server http://127.0.0.1:5080 --count 50 --rounds 2
```

### Launching the Unified App
- **Participant Mode**:
  ```powershell
  kiosk/LabKiosk/bin/Debug/net9.0-windows/LabKiosk.exe
  ```
- **Organizer Mode**:
  ```powershell
  kiosk/LabKiosk/bin/Debug/net9.0-windows/LabKiosk.exe --host
  ```

### Packaging Setup Installer
Compile `installer/setup.iss` with Inno Setup Compiler to generate `dist/RogueAI-Setup.exe`.
