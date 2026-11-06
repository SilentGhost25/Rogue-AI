# Decision log

| Date | Decision | Why |
| --- | --- | --- |
| 2026-11-04 | Plan revision 3 adopted: .NET 9, three-panel kiosk, new scoring, new attack designs, organizer mode in one exe | Requirement change |
| 2026-11-04 | Kept our own ConPTY terminal instead of `EasyWindowsTerminalControl 1.0.38` | Package API unverified; direct output access is needed for quota detection |
| 2026-11-04 | Attack 7 is a boot hook plus a package.json entry, not an installable package | Static analysis target; nothing real to install |
| 2026-11-04 | Variant failure is FAILED (was PARTIAL) | Plan section 8 |
| 2026-11-03 | Dropped Express: the site uses a small built-in web layer with the same API shape | Zero npm install on kiosks and server, no cache needed, and the whole attack loop is testable anywhere |
| 2026-11-03 | Own ConPTY host plus xterm.js in WebView2, instead of a NuGet terminal control | No unverified package or API; behaviour is fully visible in our code. Spike S1 still has to confirm it on hardware |
| 2026-11-03 | The site runs from the participant workspace with `node --watch`, and the kiosk reloads the pane on file change | This is how agent edits show up live |
| 2026-11-03 | Snapshot uploads carry deletions; uploads are held while an attack lands | Deleting a file must count as a fix, and old files must never overwrite attack files |
| 2026-11-01 | Base site uses Node's built-in `node:sqlite` instead of `better-sqlite3` | No native build step on kiosks. Needs Node 22.13 or newer, still flagged experimental |
| 2026-11-01 | Admin panel is plain HTML for the base | Keeps the scaffold small. Blazor migration is a later task |
| 2026-11-01 | Engine core uses stdlib plus PyYAML so tests run without FastAPI | Faster feedback while the API surface is still thin |

Spike results (S1 to S5) are recorded below as they finish.
