# ROGUE AI: The Breach Protocol

Everything for the live-attack event (13 Nov 2026) in one repo: a Windows app (participant kiosk and organizer mode),
an ASP.NET control plane, a Python evaluator, seven attack packs and the practice website participants defend.
The authoritative requirements are in `docs/build-plan.md` (revision 3).

## The app

`LabKiosk.exe` is one program with two modes:

| Mode | Command | What it does |
| --- | --- | --- |
| Participant kiosk | `LabKiosk.exe` | Three panels: live website, code viewer (AvalonEdit, file tree, Ctrl+S), and terminals for `agy` and `opencode`. F5 submits. Ctrl+Shift+A opens the admin console after an organizer key check. |
| Organizer | `LabKiosk.exe --host` | Starts the control plane and engine, then opens the Command Center (participant grid, scorecards, dispatch deck with target selector, pause, freeze, broadcast, exports) and can open the projector view. |

### How the live loop works
```
 agy / OpenCode (real CLIs in ConPTY terminals)  and  the code viewer (manual edits, Ctrl+S)
        |  all edit the same files in
        v
 C:\BreachWorkspace   (falls back to <app>\workspace if not writable)
   |        |       \
   |        |        `--> SnapshotSync: uploads changes and deletions every 15 s; held while an attack lands
   |        `-----------> SiteHost: `node --watch server.js` restarts on every edit
   `--------------------> watchers: reload the website pane and the code viewer, keeping cursor and scroll
```
An attack: `BREACH_IMMINENT` (10 s, siren) -> kiosk backs up to `.breach-backup/pre-attack-n` and flushes -> engine applies
the patch to the server copy (git 3-way, with an overwrite fallback that keeps the participant's version in a
`pre-attack-n` commit) -> kiosk fetches `/attack-payload/{n}` and writes the files -> site restarts, panes refresh.
Submit Fix (F5): server commits and tags the branch `participant/<id>`, the engine runs the site in an isolated copy and
runs three tiers (regression, exploit replay, hidden variants). A solve shows a glowing badge and plays a fanfare.

### Scoring (plan section 10)
+85 per solve, up to +15 time bonus (linear over the attack window), -3 for every rejected patch, and -0.2 per OpenCode
prompt beyond each attack's threshold. Every point is an append-only ledger row.

## Status: what is verified and what is not

| Part | State |
| --- | --- |
| `site/` | Zero dependencies. 7 tests pass. |
| `attacks/` | 7 packs generated from `attacks/_build.py` using the plan's target files. |
| `engine/` | **14 tests pass**, including a full run where attacks 1 to 7 land in order (overlapping files merge by patch, never by overwrite), each is exploitable once landed, the participant's reference fix is accepted and scored, and deleting features is rejected. Git branches, commits and tags are tested; the mirror push is not (no network). |
| `server/` | **Written, never compiled** (no .NET SDK was available). Ranking has xUnit tests that have not been run. |
| `kiosk/` | **Written, never compiled or run.** This includes the terminal, code viewer, organizer mode and admin window. |
| `installer/`, `build.ps1`, `run.ps1` | Written, never run. |

**About the numbers in the plan's sections 14 and 16** (clean builds, "PASS", stress-test timings): I could not reproduce
any of them here and they are not results from this repository. Treat them as targets until you have run them.

### Deliberate differences from the plan
- **Terminal:** the plan names `EasyWindowsTerminalControl 1.0.38`. I could not verify that package's API (especially how to
  read the program's output for quota detection), so the terminal is our own ConPTY host with xterm.js in WebView2. It is
  fully visible code and gives direct access to output. Swapping later is contained in `kiosk/LabKiosk/Terminal/`.
- **Website:** zero-dependency web layer instead of Express (no `npm install` anywhere, testable everywhere).
- **Attack 7** ships as a `server.js` boot hook plus a `package.json` dependency; there is no real package to install.

### First Windows run checklist
1. `dotnet build` for `server/` and `kiosk/` will probably show a few small errors. Fix those first.
2. Verify the pinned versions (WebView2, AvalonEdit 6.3.1, SignalR.Client, Microsoft.Data.Sqlite, Node, Python).
3. `scripts\vendor-assets.ps1`, then start the kiosk: confirm both terminals open and the three panes refresh on edits.
4. agy: exe path, working directory, and the exact quota-exhausted wording (`QuotaWatcher.ExhaustedMarkers`).
5. OpenCode: `attach` with `OPENCODE_SERVER_PASSWORD`, `OPENCODE_CONFIG`, and the `/session` endpoints in `OpenCodeHost`.
6. Lockdown (spike S4) is not built.

## Run it (development)
```bash
cd site && npm test && npm start                 # website, no install step
cd engine && pip install -r requirements.txt
python -m unittest discover -s tests -t .        # needs node on PATH
python attacks/_build.py                         # regenerate attack packs after editing the site or transforms
```
```powershell
dotnet test server\ControlPlane.Tests
.\run.ps1 -Organizer          # control plane + engine + Command Center (needs a build, see build.ps1)
.\run.ps1                     # participant kiosk
python sim\fake_kiosks.py --server http://127.0.0.1:5080 --count 50 --attacks 1 --admin-key <key>
.\build.ps1                   # dist\app, and dist\RogueAI-Setup.exe if Inno Setup is installed
```
The organizer key is in `%LOCALAPPDATA%\BreachKiosk\host\admin.key` (or `ROGUE_ADMIN_KEY`). Set `Breach:GitHubRepo` for the
scorecard's branch-diff link and `GIT_MIRROR_URL` (server environment only) to push participant branches.

## Known gaps
- Survival bonus and the final full re-run.
- Docker isolation for the evaluator, Semgrep/diff analysis, rank-change arrows.
- Gitea is optional and not wired; the server commits to local git and can push directly to a mirror.
- Participants can edit code by hand in the viewer (as the plan specifies), so a fix does not require an AI agent.
