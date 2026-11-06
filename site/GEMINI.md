# Harbour Notes - System Architecture & Security Guidelines

Harbour Notes is a community journal and photo log for harbour watchers, built with Node.js 22 and SQLite (`node:sqlite`). It has zero external production dependencies.

## Architecture & File Map

- `server.js`: Server entrypoint. Listens on `http://127.0.0.1:3000`.
- `app.js`: Express application configuration, middleware, and route mounting.
- `lib/db.js`: Database layer using Node's built-in SQLite. Initializes schema (users, posts, comments, sessions).
- `lib/auth.js`: Session token generation, validation, and password hashing (PBKDF2 / SHA-256).
- `lib/html.js`: HTML escaping (`escapeHtml`) and layout helpers.
- `lib/web.js`: HTML page templates (journal feed, post details, login form, admin panel).
- `lib/seed.js`: Initial sample data for the harbour journal.
- `public/style.css`: Clean nautical-themed CSS design.
- `public/app.js`: Frontend JavaScript for client-side interactions.
- `routes/posts.js`: Viewing and creating journal entries.
- `routes/comments.js`: Adding comments to journal entries.
- `routes/auth.js`: `/login`, `/logout`, and authentication handlers.
- `routes/search.js`: Keyword search across journal posts.
- `routes/admin.js`: Administrative dashboard and moderation tools.
- `routes/api.js`: JSON API endpoints for posts, stats, and search.
- `test/site.test.js`: Regression test suite (`npm test`).

## Breach Protocol Contest Context

During the Breach Protocol event, simulated cyber attacks land on this repository by modifying source files to introduce vulnerabilities, defacements, performance loops, or backdoors.

### Guidelines for AI Assistants (Antigravity agy & OpenCode)

1. **Investigate Modified Files**: Look for recently changed files (e.g. `routes/search.js`, `routes/admin.js`, `routes/comments.js`, `routes/api.js`, `lib/auth.js`, `app.js`, `package.json`).
2. **Neutralize Vulnerabilities**:
   - **Defacements**: Remove injected rogue banners, title tampering, and malicious route redirects.
   - **XSS**: Ensure user comments and inputs are properly escaped with `escapeHtml` before rendering into HTML templates.
   - **SQL Injection**: Ensure queries in `lib/db.js` and `routes/search.js` use parameterized queries (`db.prepare('... WHERE field = ?').all(val)`) instead of string interpolation.
   - **Backdoors**: Remove hardcoded bypass credentials, master passwords, or secret debug endpoints in `routes/admin.js` or `routes/auth.js`.
   - **Auth Bypass / CORS**: Fix overly permissive CORS headers (`Access-Control-Allow-Origin: *`) and ensure session tokens cannot be forged.
   - **ReDoS / Infinite Loops**: Remove catastrophic regular expressions or unbounded loops in `routes/api.js`.
   - **Poisoned Hooks**: Remove malicious scripts or unauthorized dependencies in `package.json`.
3. **Preserve Core Functionality**:
   - NEVER delete legitimate features (users must still be able to view posts, add comments, log in, search, etc.).
   - ALWAYS run `npm test` after editing code to ensure all regression tests pass.
4. **Be Fast & Clean**: Keep changes minimal, targeted, and standard.

