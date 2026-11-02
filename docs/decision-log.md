# Decision log

| Date | Decision | Why |
| --- | --- | --- |
| 2026-11-01 | Base site uses Node's built-in `node:sqlite` instead of `better-sqlite3` | No native build step on kiosks. Needs Node 22.13 or newer, still flagged experimental |
| 2026-11-01 | Admin panel is plain HTML for the base | Keeps the scaffold small. Blazor migration is a later task |
| 2026-11-01 | Engine core uses stdlib plus PyYAML so tests run without FastAPI | Faster feedback while the API surface is still thin |

Spike results (S1 to S5) are recorded below as they finish.
