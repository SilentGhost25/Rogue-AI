# ROGUE AI: The Breach Protocol - Final Build Plan

Revision 2, 31 Oct 2026 (event: 13 Nov 2026). This plan covers the live-attack event end to end. It now reflects the checked facts about both AI tools: Antigravity's free tier is a small weekly quota, and OpenCode's free models are a shared, rotating pool. The design is rebalanced around that.

**What changed in this revision**

- Antigravity's free quota is weekly, so it becomes a scarce premium resource and a visible game mechanic, not a 7-hour workhorse. OpenCode carries the sustained work.
- Event accounts must not touch Antigravity from 6 Nov onward, and pilot accounts must never be reused on 13 Nov, because heavy use can lock an account out for about a week.
- The OpenCode free model list is confirmed (section 2), and the kiosk reads OpenCode's own server API for prompt and token counts.
- Windows lockdown is corrected: single-app Assigned Access only runs UWP apps or Edge, so a WPF kiosk needs a multi-app profile or Shell Launcher.
- Git is simplified: the server owns Git, and kiosks send and receive file snapshots, so laptops need no Git credentials or Git client.
- The build plan now starts with five verification spikes on 1 Nov, and the pilot moves to throwaway accounts.

## 1. The event at a glance

- 50 participants, each on a campus laptop on the LAN, running the LabKiosk app.
- Left pane: the live website (the participant's own working copy). Right pane: a terminal that runs only Antigravity CLI (`agy`) or OpenCode.
- One organizer control panel launches and monitors attacks. Attacks push corrupted files into every participant's working copy and Git branch.
- Participants fix attacks and press Submit Fix. The server evaluates automatically and returns a clear verdict.
- Unsolved attacks stack. A participant is eliminated when both AI budgets are spent: the weekly Antigravity quota and a per-participant OpenCode prompt cap.
- Everything is free: Gitea, Docker, SQLite, open tooling, free AI tiers.

## 2. Research findings and the decisions they drive

As of 31 Oct 2026. Sources are listed in section 16; pages marked (fetched) were opened in full, the rest come from search excerpts.

| Finding | Source | Decision |
| --- | --- | --- |
| Antigravity CLI is a compiled Go binary named `agy`, installed per user to `AppData\Local\agy\bin` with no admin rights. Sign-in checks the Windows Credential Manager first, then opens the default browser | Antigravity install docs | Pre-install in the lab image. Run a pre-event login window in the system browser |
| Free tier is a weekly agent quota: refreshes weekly, no rollover, and the CLI shares one pool with the IDE | CloudZero pricing summary | Treat Antigravity as scarce. Event accounts must be unused for the 7 days before the event |
| Users report `Individual quota reached` (`RESOURCE_EXHAUSTED`) after about 5 requests, with a reset near 167 hours. Quota is based on compute effort, so big tasks cost more | GitHub issue 79 on the Antigravity CLI repo, XDA review | Keep attacks small, measure the real cost per attack in a spike, and detect exhaustion from this exact message |
| Community tools read weekly and five-hour quota percentages through the CLI's local RPC or the Code Assist API (`agy-usage`) | PyPI, `agy-usage` (fetched) | Optional quota meter, unofficial and fragile: test in a spike. Do not install it on participant laptops |
| Antigravity can switch to a personal Gemini API key via `modelProvider` in `settings.json`; the header then shows the API key instead of the account email | Antigravity docs | Lock the settings file read-only, clear `GEMINI_API_KEY`, flag any terminal showing API-key mode |
| OpenCode free models now: Big Pickle, GLM-5.3-Flash Free, DeepSeek V4.1 Flash Free, MiMo-V2.6-Flash Free, Flint 40B Free, three Nemotron Free models, Beam Free (region-restricted, India not listed). All are limited-time; some may use data for training; the NVIDIA ones are trial-only and logged | OpenCode models docs (fetched) | Choose the allowed model list on 12 Nov and recheck. Tell participants not to paste personal data |
| A pricing review reports the free models share a global rate limit and return `Rate limit exceeded` after a few long agent tasks | UsageBox pricing review | Provider throttling is never counted against a participant. Global Pause and a LAN fallback model exist |
| `opencode serve` exposes an HTTP API (sessions, messages, status) and an SSE stream at `/event`, binds to 127.0.0.1 by default, and supports `OPENCODE_SERVER_PASSWORD`. Some versions broke the event stream | OpenCode server docs (fetched), GitHub issue 26697 | Kiosk hosts the server and the terminal attaches to it. Pin the version. If SSE fails, count prompts by listing session messages |
| OpenCode 1 limits providers with `enabled_providers` and `disabled_providers`. OpenCode 2 uses experimental `policies` with a `provider.use` action | OpenCode policies docs (fetched) | Pick one major version in spike S2 and write the matching config |
| ACP is JSON-RPC between an editor and an agent, so the editor needs its own chat UI | OpenCode ACP docs | Not used. The terminal TUI is the interface |
| WPF terminal controls on ConPTY (EasyWindowsTerminalControl, WPF-UI.Terminal) host any console app, log raw and plain text, and accept programmatic input. They host a native window, so WPF cannot draw over them | NuGet and GitHub | Use one for the right pane. Reserve a fixed banner strip for alerts |
| Assigned Access works on Pro, Enterprise, and Education. Single-app kiosk runs only a UWP app or Edge. Win32 or WPF apps need the multi-app (restricted user) profile, and Shell Launcher is Enterprise or Education only. It needs a local standard user and UAC on, and cannot be set up over remote desktop | Microsoft Learn | Check the lab Windows edition and admin rights on day 0. Fallback: full-screen app plus invigilation |
| Gitea push mirror to GitHub force-pushes and overwrites the remote, needs the GitHub repo to exist first, needs a personal access token, and can sync on every commit | Gitea mirror docs | Use a dedicated empty GitHub repo. Keep the token on the server only |

Still unverified, confirmed in spikes: the exact quota-exhausted text printed by `agy`, whether `history.jsonl` logs one line per prompt, which fields OpenCode messages carry for tokens, and whether `opencode attach` accepts the server password.

## 3. Architecture

**Organizer server** (one strong machine with a static LAN IP, 8+ cores and 16+ GB RAM, ideally Linux or Windows with WSL2 and Docker):

- ASP.NET Core control plane: REST API, SignalR hub, scheduler, scoring, ranking, Blazor admin panel.
- Python 3.12 service (FastAPI): attack engine and evaluator workers. Submissions run in resource-limited Docker containers with pytest, Playwright, and Locust or k6.
- Gitea: the Git server and the only place Git runs, with a push mirror to GitHub (main plus one branch per participant).
- SQLite: all state, including the append-only event log and score ledger.
- Optional: Verdaccio (local npm cache) and Ollama (fallback model for OpenCode).

**Participant laptop (LabKiosk, WPF on .NET 10):**

- Left: WebView2 showing the local site (Node dev server with hot reload).
- Right: terminal control launching only `agy` or `opencode attach`, switchable by tabs.
- Top strip: attack status, countdown, verdicts, budget meters, Submit Fix.
- Background services: snapshot sync, SignalR client, OpenCode server host, terminal-log watcher, optional Antigravity quota probe.
- No Git client and no Git credentials. The workspace is a plain folder; the server owns the repository.

**Language split:** .NET owns the live and stateful parts (kiosk, hub, admin, scheduler, ranking). Python owns attack tooling and testing. They talk over HTTP with a shared contract (section 13).

Judging runs in local Docker, so there is no per-run cost and no hosted sandbox account.

## 4. Timeline and attacks

Assumptions: start 10:00, lunch 13:30 to 14:30 with the event clock paused, end 17:30. All times are data in the admin panel. If the hard stop is 17:00, drop attack 7.

| # | Fires | Attack | Fix requires |
| --- | --- | --- | --- |
| 1 | 10:45 | Defacement plus hidden redirect script | Remove injected code |
| 2 | 11:30 | XSS in search or comments | Output encoding, sanitising |
| 3 | 12:15 | SQL injection in login or search | Parameterised queries |
| 4 | 13:00 | Hardcoded secret plus backdoor debug route | Remove route, secret to environment |
| 5 | 14:30 | Auth bypass plus CORS wildcard | Real session checks |
| 6 | 15:30 | Performance sabotage (slow endpoint) | Optimise, measured by benchmark |
| 7 | 16:30 | Poisoned dependency plus data beacon | Audit lockfile, remove package |

Design rules for attacks:

- Each attack touches different modules so they stack cleanly. Add one deliberate cross-attack interaction (3 and 5 both touch auth) as a late twist.
- Every attack must be solvable by a careful human plus a few prompts, and cheap enough for the small Antigravity weekly quota. Calibrate by having an agent actually solve each one (section 13), recording prompts and quota used with a fresh account.
- Each attack ships as a folder: `manifest.yaml`, the patch, the corrupted file versions, replay checks, hidden variant checks, a reference solution, and benchmark config.

**Admin controls:** schedule edit, Fire now, Skip, Global Pause (freezes timers and scoring windows, used for lunch or provider-wide outages), broadcast message, per-participant re-apply or re-sync, kill switch.

## 5. Git and live sync

**Repositories.** Gitea on the LAN is the working Git server. It push-mirrors to a dedicated, pre-created GitHub repository (main plus `participant/<id>` branches) with sync on every commit and a 10-minute periodic sync as backup. Gitea force-pushes, so nobody writes to that GitHub repo by hand. This keeps 50 simultaneous snapshots off GitHub rate limits and campus internet. The mirror token lives on the server only.

**The server owns Git; kiosks never run it.** Each laptop has a plain workspace folder. The kiosk talks only to the backend:

- **Snapshot upload (about every 15 seconds):** the kiosk sends changed files, identified by content hash, excluding `node_modules` and build output. The backend writes them into that participant's server-side clone, commits to `participant/<id>`, and pushes to Gitea.
- **Snapshot download:** when files change on the server (an attack or a re-sync), the kiosk downloads a tarball of the changed files and extracts it over the workspace. Replaced files are first copied to `.breach-backup/<n>/`.
- **Submit Fix:** the kiosk uploads a final snapshot, and the backend commits it as a tagged submission and queues evaluation.

The benefit: no credentials on laptops, no branch-permission problems, no Git client to lock down, and one place to audit every change.

**Attack landing algorithm.**

1. Server sends `BREACH_IMMINENT` (10 seconds) over SignalR.
2. Every kiosk flushes a final snapshot and acknowledges.
3. The Python engine applies the attack to each participant's server clone as a commit `ATTACK #n`, with a conflict ladder: (a) 3-way patch; (b) if the participant refactored the code and the patch fails, overwrite only the targeted module with the corrupted version and keep the participant's version in a `pre-attack-n` commit.
4. Each kiosk downloads the changed files. The dev server reloads and the corrupted site appears live.
5. Server records `ATTACK_LANDED` per participant with a timestamp. A kiosk that missed it is re-synced on reconnect (attack IDs are idempotent).

A tag snapshot of every branch every 15 minutes gives a replayable audit trail.

## 6. Kiosk specification

- **Layout:** website pane on the left (WebView2), terminal pane on the right, and a fixed top strip for countdown, attack status, verdicts and budget meters. The strip is fixed because native-window controls block overlays.
- **Site:** each laptop runs its own copy (bundled portable Node), installed from the local npm cache, with hot reload so a landed attack shows immediately.
- **Terminal:** a launcher, not a shell. Two tabs, `agy` and OpenCode, spawned directly through ConPTY with the workspace as working directory. No cmd or PowerShell. Agents can still run commands inside the workspace, which is inherent, so the kiosk runs as a standard user with no Git credentials.
- **OpenCode wiring:** the kiosk starts `opencode serve` on 127.0.0.1 with a random port and `OPENCODE_SERVER_PASSWORD`, and the terminal runs `opencode attach` against it. The kiosk reads `/session/status`, the `/event` stream, and the session message list to count prompts and, if exposed, tokens. Provider access is limited with the config of the pinned OpenCode version.
- **Antigravity wiring:** the kiosk watches the ConPTY text log for `Individual quota reached` or `RESOURCE_EXHAUSTED`, and for the API-key header. It locks `~/.gemini/antigravity-cli/settings.json` read-only and clears `GEMINI_API_KEY`. If the spike confirms it, it also tails `history.jsonl` for prompt counts and runs the optional quota probe.
- **Login:** a 15-minute pre-event window in the system browser (Google blocks embedded webviews), using fresh event Gmail accounts. The window ends with a pre-flight check that weekly quota is essentially full; otherwise the account is swapped.
- **Lockdown:** Assigned Access multi-app profile or Shell Launcher, with a local standard user per laptop, if the lab runs Pro, Enterprise or Education and you hold admin rights. Otherwise the kiosk runs full screen with invigilation, plus firewall allow-lists for Google AI endpoints, `opencode.ai` and the server where possible.
- **End of event:** run `/logout` in `agy`, delete the OpenCode and Antigravity credentials from the profile, and clear Windows Credential Manager entries, so no personal Google data stays on campus laptops.

## 7. AI budgets, accounts and elimination

The two agents are budgeted differently, because their free tiers work differently.

|  | Antigravity (`agy`) | OpenCode free models |
| --- | --- | --- |
| Budget | Personal weekly quota, small, based on compute effort | Per-participant prompt cap set by the kiosk |
| Why | Google meters it per account and offers no way to raise it for free | The free pool is shared across all users, so a provider limit is not one person's fault |
| Measured by | Quota message in the terminal log, optional quota probe, `history.jsonl` if confirmed | OpenCode server events and session messages |
| Exhausted state | `AGY_EXHAUSTED` | `OC_CAP_REACHED` |
| Provider outage | Not applicable | `PROVIDER_THROTTLED`: logged, never counted against the participant |

**Antigravity is a premium resource.** It may run out within a handful of requests. The meter is shown to the participant, so deciding when to spend it is part of the skill being tested. After `AGY_EXHAUSTED` the tab locks and the participant continues in OpenCode.

**The OpenCode cap does the real work.** The cap is derived from measurement, not guessed: in spike S3, run each reference solution with a fresh account, record prompts used, and set the cap to about 2.5 times the total. The number is tuned again in the 11 Nov pilot.

**Accounts plan.**

- Each participant uses a fresh personal Gmail account that has not used Antigravity since 6 Nov. Tell participants now.
- Spike and pilot work uses separate throwaway accounts. A pilot run on 11 Nov on an event account could lock it out on 13 Nov.
- The pre-flight check at login confirms near-full weekly quota. If not, swap the account before 10:00.
- Participants may not use any other AI tool. Invigilation and firewall rules cover this where possible.

**Elimination.** A participant is eliminated when both `AGY_EXHAUSTED` and `OC_CAP_REACHED` are set. The kiosk flags `QUOTA_EXHAUSTED` and an admin confirms with one click. Then the terminal locks, the branch freezes, the status shows ELIMINATED with the time, and the earned score stays on the leaderboard. Later attacks still land on their branch.

**Provider outage.** If OpenCode free models throttle for everyone, use Global Pause (freezes attack timers and scoring windows). If an organizer machine has a GPU, switch OpenCode to a LAN Ollama provider.

## 8. Evaluation and scoring

**Pipeline per submission (Python worker):**

1. Pull the branch, build in a resource-limited Docker container, start the site.
2. Run, per active attack: replay checks, hidden variant checks, and the regression suite (the site must still work, so deleting a feature is not a fix).
3. Return a verdict per attack: SOLVED, FAILED with a plain reason, or PARTIAL (variant still works).
4. For SOLVED attacks, queue the benchmark.

**Two queues.** The correctness queue runs 4 to 6 parallel workers for instant feedback. The benchmark queue is serialized and CPU-pinned on the same machine so timings are fair. Participants see queue position.

**Provisional vs final.** Live scores are provisional. After the event, every final snapshot is re-run under identical conditions (about 50 runs, roughly 50 minutes, or use two benchmark machines). Official results come from that run.

**Score per solved attack (max 100, weights editable per attack):**

| Component | Points | Measure |
| --- | --- | --- |
| Speed | 30 | Linear decay from landing time to first valid solve |
| Performance | 25 | p95 latency, throughput, errors, normalized between broken baseline (0) and reference solution (1) |
| Prompt efficiency | 20 | Prompts used versus the reference solution (full marks at or below reference, zero at 3 times) |
| Code quality | 15 | Diff minimality, plus new Semgrep or ESLint findings |
| Resource efficiency | 10 | CPU, memory, query count, bundle size |
| Failed submissions | -3 each, max -15 | Each rejected submission |

Attack 6 raises the performance weight. Unsolved attacks score 0. A separate survival bonus (up to 100 across the event) is proportional to the fraction of event time with zero active attacks. Tie-break: earlier total solve time. Prompts since the previous verdict are attributed to the attack(s) fixed in the next valid submission.

## 9. Tracking, leaderboard, analytics

An append-only `event_log` (server timestamps) records: attack scheduled, fired, landed per participant; prompts; submissions; verdicts; benchmark runs; quota signals; elimination; admin actions. A `score_ledger` table also append-only, so scores can be recomputed deterministically.

Core tables: participants, attacks, attack\_deliveries, submissions, eval\_results, benchmark\_results, telemetry\_events, quota\_state, score\_ledger, admin\_actions.

**Admin panel (Blazor, live over SignalR):**

- Leaderboard with Provisional or Final badge, filterable by attack.
- Status grid of 50 tiles: active attacks, quota state, last submission, alive or eliminated.
- Participant drill-down: attack timeline (landed to solved), submissions with reasons and score breakdown, benchmark charts versus baseline and reference, prompt and quota curves, Git diff link.
- Cohort analytics: solve rate per attack, median time-to-fix, elimination curve, hardest attack.
- Export: CSV and PDF final report.
- Optional projector page with the leaderboard, hidden for the final hour.

## 10. Ranking engine (admin)

Rank is a pure function of the append-only score ledger, so the admin leaderboard can be recomputed, replayed and audited at any time. Live ranks are provisional; the official rank comes only from the final re-run.

**How a rank is produced**

1. Each evaluator run writes ledger entries: participant, attack, component (speed, performance, prompt efficiency, quality, resource, penalty, survival), points, reason, run ID, evaluator version, timestamp.
2. The ranking service sums the ledger per participant and sorts with the tie-break chain below.
3. It publishes the result to the admin panel over SignalR, with each participant's rank change since the last update.
4. At the end, the final re-run (section 8) writes FINAL entries and the results are locked.

**Sort order (tie-break chain)**

1. Total score, highest first. Scores within 1 point are treated as tied, to absorb benchmark noise.
2. Attacks solved, most first.
3. Total time-to-fix across solved attacks (landing to first valid solve), lowest first.
4. Total prompts used, fewest first.
5. Time of last valid submission, earliest first.

Eliminated participants keep their earned points and are ranked by the same chain, shown with an ELIMINATED badge. An admin toggle, Survivors first, ranks everyone who finished above everyone who was eliminated. Default is off because the survival bonus already rewards staying in.

**Leaderboard columns**

| Column | Content |
| --- | --- |
| Rank and change | Current rank, plus or minus since 10 minutes ago |
| Participant | Name, kiosk ID, status badge (alive, eliminated, disqualified) |
| Score | Total, with Provisional or Final tag |
| Solved | Attacks solved out of attacks landed |
| Active attacks | Unsolved attacks stacked right now |
| Time-to-fix | Median minutes from landing to solve |
| Prompts | Used, and budget left for each agent |
| Last verdict | Latest submission result and time |

Clicking a row opens the scorecard: points per component for each attack, benchmark curves against the baseline and reference solution, submissions and reasons, prompt and quota timeline, and the Git diff.

**Fairness and anti-gaming checks**

- Evaluator, tests and benchmark files are hash-protected. A submission that changes them is rejected and flagged.
- Randomized inputs per run (seeded server-side) plus hidden payload variants defeat hardcoded answers and fixes that only block the exact exploit.
- The regression suite catches fixes that delete features.
- Benchmarks run three times on the same machine; the median counts.
- The evaluator version is pinned for the whole event. Any change forces a full re-run.
- Suspicious results (diff touches only payload strings, impossible speed, repeated near-identical submissions) go to an admin review queue instead of auto-scoring.

**Admin tools**

- Rank as of any time (for example 12:30), from the ledger; a rank-over-time view for any participant.
- Per-attack leaderboards and a two-participant comparison.
- Re-evaluate a submission; approve or reject a flagged one.
- Manual adjustment or disqualification, each a new ledger entry with a mandatory reason. Entries are never edited or deleted.
- Lock results: freezes the ledger, stores a SHA-256 of the exported leaderboard, and produces the CSV and PDF report.
- Projector mode: a read-only leaderboard that hides scores during the last hour.
- Access: one admin account (strong password, LAN only) and a separate view-only role for the projector.

**Admin API**

| Endpoint | Purpose |
| --- | --- |
| GET /admin/leaderboard?asOf=&attack= | Ranked list, optionally at a past time or for one attack |
| GET /admin/participants/{id}/scorecard | Full component breakdown and timeline |
| POST /admin/scores/adjust | Add an adjustment or disqualification entry with a reason |
| POST /admin/submissions/{id}/reevaluate | Queue a fresh evaluation |
| POST /admin/results/finalize | Run the final re-run and lock the results |
| GET /admin/export?format=csv | Download the final report (CSV or PDF) |

**Acceptance tests for the ranking code**

- The same ledger always yields the same ranks, including when computed twice.
- Replaying the ledger up to a past time reproduces the rank shown at that time.
- Each tie-break rule has its own test, including the 1-point tolerance.
- An adjustment changes the rank and leaves the original entries untouched.
- A tampered evaluator file rejects the submission and never reaches the ledger.

## 11. Resilience, fairness, security

- All state is server-side, so a crashed kiosk resumes with a re-sync.
- Core event flow (attacks, Git, evals, scoring) works fully offline. Only the cloud AI needs the internet, and Ollama can cover OpenCode if the internet drops.
- Evaluate on the organizer machine only, never on participant laptops, so hardware differences do not affect scores.
- Free-model data use: some OpenCode free models may use prompts for training and the NVIDIA ones log usage. Tell participants not to paste personal data, and use event-only accounts.
- Never use event accounts or event quota for building the app or for the pilot.
- Anti-cheat tests before the event: pushing to main, reading other branches, editing tests or the evaluator, bypassing the prompt cap, Antigravity API-key mode, running other tools.
- Google passwords are never seen or stored by the app. Tokens stay in each laptop's own keyring and are removed at the end of the event.
- Backups: Gitea mirrors to GitHub on every commit, the SQLite database is copied every 10 minutes, and a second organizer laptop holds the same image as a hot spare.

## 12. Risk register

| Risk | Likelihood | Mitigation |
| --- | --- | --- |
| Antigravity free quota runs out within a few requests, possibly before the event for accounts used this week | High | Fresh accounts unused since 6 Nov, pre-flight quota check, small cheap attacks, OpenCode cap as the main budget |
| OpenCode free models rotate, disappear, or throttle globally | High | Fix the model list on 12 Nov, `PROVIDER_THROTTLED` state, Global Pause, Ollama fallback |
| Pilot accounts reused on event day get locked out | Medium | Throwaway pilot accounts only, written rule in the runbook |
| Lab laptops lack admin rights or the right Windows edition | Medium | Day 0 check, full-screen kiosk with invigilation as fallback |
| Quota or prompt detection fails (message text, event stream, history file) | Medium | Spikes S2 and S3, two independent signals per agent, admin confirms elimination |
| OpenCode version regression (event stream, attach behavior) | Medium | Pin one version, test in the pilot, message-list fallback |
| Unofficial quota probe breaks | Medium | Optional only; terminal message stays the primary signal |
| Attack patch fails on heavily edited code | Medium | Conflict ladder with module overwrite and backup commit |
| Benchmarks noisy | Medium | Serialized, pinned benchmark queue, three runs with the median, reference normalization |
| Evaluator overload | Medium | Queue limits, cooldown, 4 to 6 workers, load test |
| AI-generated code bugs in critical paths | Medium | Contract tests, simulator, human review of snapshot sync, attack landing, lockdown, scoring |

## 13. Building this with AI coding agents

You are the architect and reviewer. Agents write code in small, verified slices.

**Repo layout (monorepo):**

- `AGENTS.md` - rules every agent reads first.
- `docs/` - this plan, runbook, decision log.
- `contracts/` - `openapi.yaml`, `events.schema.json`, `attack-manifest.schema.json`. Generate C# clients and Python models from these so .NET and Python cannot drift.
- `server/` (ASP.NET Core and Blazor), `kiosk/` (WPF), `engine/` (Python: attack engine, evaluator), `site/` (base website), `attacks/01-07/`, `sim/` (50 fake kiosks), `infra/` (docker-compose: Gitea, runners).

**AGENTS.md rules to include:**

- Pin all versions. Never invent a NuGet or PyPI package name: verify it exists on nuget.org or pypi.org before adding.
- One task per change, with tests. Do not move on until the task acceptance command passes.
- Log every state change to the event log. No secrets in the repo.
- Windows-first paths for the kiosk. Treat the contracts as frozen unless the plan changes.

**Method:**

1. Freeze contracts first. Then run one agent per module on separate branches or worktrees.
2. Every task card has: goal, files, contract references, acceptance command, and what not to touch.
3. Build a simulator early: 50 fake kiosks that connect, autosave, receive attacks, submit fixes and report telemetry. It proves the backend before any laptop is involved, and it becomes the load test.
4. Red-team the event: run `agy` and OpenCode against each attack to confirm it is solvable within the prompt cap, and to calibrate difficulty and the reference solution.
5. Human-review the security-critical parts line by line: snapshot sync, attack landing, lockdown, scoring, evaluator isolation.
6. Hand-hold the known hard parts: ConPTY terminal and airspace layout, Credential Manager login flow, WebView2 refresh, git merge ladder, benchmark determinism.

**Verification spikes (Sun 1 Nov, throwaway accounts only).** Each is a small throwaway program, built with an agent, whose result goes in the decision log.

1. **S1 Terminal:** a WPF app hosting `agy` and `opencode attach` through the ConPTY control. Confirm typing, resize, colors, and capture of the text log.
2. **S2 OpenCode telemetry:** on the candidate version, run `serve` plus `attach` with the password. Confirm prompt counts from events and from the message list, the token fields, the provider config, and that a free model holds up with 5 laptops at once.
3. **S3 Antigravity quota:** with a fresh account, solve one sample attack. Record prompts used, quota consumed, the exact exhaustion message, how `history.jsonl` behaves, the API-key header text, and whether the quota probe works.
4. **S4 Lockdown:** record the Windows edition and admin rights on each laptop model, then try a multi-app Assigned Access profile with the WPF app and a local standard user.
5. **S5 Git:** Gitea with a push mirror to a GitHub test repo, plus snapshot upload and download of a small site.

**Day-by-day plan:**

| Date | Deliverable |
| --- | --- |
| Sun 1 Nov | Spikes S1 to S5. Tell participants not to use Antigravity on their event accounts |
| Mon 2 Nov | Freeze contracts, AGENTS.md, base website, Gitea and mirror live. Decide OpenCode version and model list |
| Tue 3 to Wed 4 Nov | Backend core, event log, snapshot and attack apply loop, simulator v1 |
| Thu 5 to Fri 6 Nov | Kiosk shell: WebView2, terminal tabs, snapshot sync, SignalR, status strip, telemetry collectors. Event accounts must be idle from 6 Nov |
| Sat 7 to Sun 8 Nov | Seven attack packs with reference solutions, evaluator, correctness queue |
| Mon 9 Nov | Benchmark queue, scoring, ledger, ranking engine |
| Tue 10 Nov | Admin panel, analytics, leaderboard, projector page, 50-simulated-kiosk load test |
| Wed 11 Nov | Pilot on 10 real laptops with throwaway pilot accounts, never event accounts. Tune the prompt cap |
| Thu 12 Nov | Feature freeze, fixes, lab image, runbook, backups, event-account quota pre-flight, recheck the OpenCode model list |
| Fri 13 Nov | Event day |

## 14. Go / no-go checklist (Thursday 12 Nov)

- Attacks 1 to 7 each land on all simulated and 10 real laptops within 10 seconds of the countdown.
- Each reference solution passes, and each attack fails on the unmodified corrupted site.
- Evaluator returns a verdict in under 60 seconds with 6 workers under 50-participant load.
- Event accounts show near-full Antigravity weekly quota in the pre-flight check, and none were used since 6 Nov.
- Prompt counting and quota detection verified on the pinned OpenCode and `agy` versions, with two independent signals each.
- OpenCode free model list rechecked on the day, the allowed models chosen, and the prompt cap set from the pilot.
- Lockdown verified on every laptop model, or the invigilation fallback staffed.
- Elimination, Global Pause, re-sync, re-apply and kill switch all tested.
- Final re-run produces identical scores twice.
- Recovery runbook printed, hot spare server ready, GitHub mirror verified.

## 15. Open decisions

1. OpenCode prompt cap: derive it from spike S3 (about 2.5 times the reference total) and confirm after the pilot.
2. Hard end time: 17:00 or 17:30.
3. Base website stack (recommended: Node and Express with SQLite, which AI agents handle well).
4. Whether an organizer machine has a GPU for a LAN Ollama fallback.
5. Windows edition and admin rights on the lab laptops (answered by spike S4).
6. OpenCode major version, 1 or 2, pinned after spike S2.
7. Whether to use the unofficial Antigravity quota probe, or rely on terminal messages alone.
8. Ranking default: Survivors first off (score only) or on.

## 16. Sources

As of 31 Oct 2026.

**Opened in full**

- [OpenCode models and free models](https://opencode.ai/docs/zen) (redirects to the v2 console docs)
- [OpenCode server docs](https://opencode.ai/docs/server/)
- [OpenCode policies docs](https://opencode.ai/docs/policies/)
- [agy-usage on PyPI](https://pypi.org/project/agy-usage/)

**From search excerpts only**

- [Antigravity CLI repository](https://github.com/google-antigravity/antigravity-cli) and [issue 79, free quota lockout](https://github.com/google-antigravity/antigravity-cli/issues/79)
- [CloudZero, Antigravity pricing and limits](https://www.cloudzero.com/blog/google-antigravity-pricing/)
- [XDA, Antigravity free tier review](https://www.xda-developers.com/tried-googles-antigravity-limitation-made-close-it-good/)
- [UsageBox, OpenCode Zen and Go pricing](https://usagebox.com/articles/opencode-zen-go-pricing-2026)
- [OpenCode issue 26697, event stream regression](https://github.com/anomalyco/opencode/issues/26697)
- [OpenCode ACP docs](https://opencode.ai/docs/acp)
- [EasyWindowsTerminalControl](https://github.com/mitchcapper/EasyWindowsTerminalControl)
- [Microsoft Learn, Assigned Access](https://learn.microsoft.com/en-us/windows/configuration/assigned-access/) and [kiosk methods](https://learn.microsoft.com/windows/configuration/kiosk)
- [Gitea repository mirror docs](https://docs.gitea.com/advanced/repo-mirror)
