# Harbour Notes - Agent Instructions

This repository is Harbour Notes, a Node.js 22 + SQLite web application for harbour watchers.

## Codebase Map
- `server.js`: Web server startup (port 3000)
- `app.js`: Application middleware and route setup
- `lib/db.js`: SQLite database schema and queries
- `lib/auth.js`: User authentication & session management
- `lib/html.js`: HTML escaping and templates
- `lib/web.js`: Page rendering
- `routes/`: Express route handlers (`posts.js`, `comments.js`, `auth.js`, `search.js`, `admin.js`, `api.js`)
- `test/site.test.js`: Automated tests (`npm test`)

## Security Remediation Tasks
Attacks will alter files to create vulnerabilities. When the user asks for assistance:
1. Examine the active attack report.
2. Inspect the relevant route or library file.
3. Fix the vulnerability cleanly without removing legitimate user features.
4. Run `npm test` to verify that no regressions were introduced.

