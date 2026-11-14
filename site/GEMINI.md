# ROGUE AI — DEFENDER AGENT WORKSPACE DIRECTIVE

You are assisting a participant in defending the AEGIS Municipal Core web service against active cyber intrusions.

## Architecture & Codebase Layout
- **Runtime**: Node.js (Express, built-in SQLite).
- **Entry point**: `server.js` (listens on `http://127.0.0.1:3000`).
- **Application setup**: `app.js`.
- **Subsystem Routes** (`routes/`):
  - `overview.js`: Municipal telemetry overview, system status.
  - `traffic.js`: Traffic management grid and sensor telemetry.
  - `power.js`: Power grid distribution and emergency override controls.
  - `vault.js`: Secure records and credential vault.
  - `security.js`: Security audits, alerts, and lockdown triggers.
  - `auth.js`: User authentication, session tokens, and RBAC.
  - `admin.js`: Administrative panel and operator switches.
  - `settings.js`: Municipal node settings and subsystem configs.
- **Core Libraries** (`lib/`):
  - `auth.js`, `cache.js`, `config.js`, `db.js`, `diagnostics.js`, `html.js`, `permissions.js`, `rate-limiter.js`, `reports.js`, `telemetry.js`.

## Critical Operational Rules
1. **Scope & Touch Penalty**: ONLY modify files directly related to the active defect/breach. Modifying files outside the breach scope triggers an automatic touch penalty (-300 pts) and blocks submission.
2. **Preserve Runtime Stability**: Ensure `node server.js` boots cleanly without syntax errors, unhandled rejections, or crashes.
3. **No Secrets or Exploitation**: Fix the vulnerability cleanly with defensive programming. Do not introduce backdoor bypasses.
4. **Git Usage**: You can use `git status` and `git diff` locally to inspect incoming attack modifications.

