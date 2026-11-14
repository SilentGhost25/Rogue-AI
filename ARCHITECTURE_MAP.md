# ROGUE AI — ARCHITECTURE & COMMUNICATION MAP

## 1. System Overview & Component Diagram

```
+----------------------------------------------------------------------------------------------------+
|                                    ORGANIZER HOST ENVIRONMENT                                      |
|                                                                                                    |
|   +------------------------------------+          +--------------------------------------------+   |
|   |   Control Plane (ASP.NET Core 9)   |          |      Breach Engine (FastAPI / Python)      |   |
|   |   Port: 5080 (REST + SignalR Hub)  |  HTTP    |      Port: 8000                            |   |
|   |   - RogueAgentOrchestrator         |<-------->|      - 33 Attack Manifests & Payloads      |   |
|   |   - BackupService (SQLite Snapshots|          |      - Win32 Job Object Evaluator Sandbox  |   |
|   |   - HostDiscoveryBroadcaster (UDP) |          |      - Latency & Resource Benchmarking     |   |
|   +------------------------------------+          +--------------------------------------------+   |
|          ^                  ^                                  ^             ^                     |
|          |                  |                                  |             |                     |
|     REST | SignalR          | REST                             |             | Execution           |
|          v                  v                                  v             v                     |
|   +---------------+  +------------------+          +-----------------------------------+           |
|   |  SQLite DB    |  | Workspaces Store |          | Disposable Sandboxes (Slot1..4)   |           |
|   |  (WAL Mode)   |  | data/workspaces/ |          | C:\BreachSandboxes / slot dirs    |           |
|   +---------------+  +------------------+          +-----------------------------------+           |
+----------------------------------------------------------------------------------------------------+
       ^                     ^
       | UDP 5082 (Beacon)   | HTTP 5080 (REST) + WSS 5080 (/hub)
       v                     v
+----------------------------------------------------------------------------------------------------+
|                                   PARTICIPANT LAPTOP / KIOSK                                       |
|                                                                                                    |
|   +--------------------------------------------------------------------------------------------+   |
|   |                          LabKiosk (WPF / .NET 9.0 Windows Client)                          |   |
|   |                                                                                            |   |
|   |   [ PANE 1: WEB ]                [ PANE 2: CODE ]               [ PANE 3: AI ]             |   |
|   |   WebView2 Runtime               AvalonEdit Text Editor         WebView2 + xterm.js        |   |
|   |   http://127.0.0.1:3000          Syntax highlighted             ConPTY Terminal Bridge     |   |
|   |   AEGIS Defense UI               C:\BreachWorkspace             opencode serve / agy CLI   |   |
|   +--------------------------------------------------------------------------------------------+   |
|          ^                                                                  ^                      |
|          | HTTP 3000                                                        | Port 4096            |
|          v                                                                  v                      |
|   +-------------------------------+                              +---------------------+           |
|   | Local Site Host (node --watch)|                              | OpenCode Server     |           |
|   | C:\BreachWorkspace\server.js  |                              | Loopback auth token |           |
|   +-------------------------------+                              +---------------------+           |
+----------------------------------------------------------------------------------------------------+
```

---

## 2. Network Boundaries & Port Mappings

| Service | Port | Binding | Protocol | Intended Scope | Security Boundary |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **Control Plane** | `5080` | `0.0.0.0` | HTTP / WS | Organizer LAN | Protected by `X-Admin-Key`, Desk Token, & Cookies |
| **Breach Engine** | `8000` | `127.0.0.1` | HTTP (REST) | Host Loopback Only | Internal backend proxy only; never exposed to LAN |
| **LAN Auto-Discovery** | `5082` | `0.0.0.0` | UDP Broadcast | Local Subnet | Unicast ping `ROGUE_AI_DISCOVERY_PING` -> Host Beacon |
| **Local AEGIS Site** | `3000` | `127.0.0.1` | HTTP | Kiosk Loopback | Ephemeral participant defense node (`node --watch`) |
| **OpenCode Daemon** | `4096` | `127.0.0.1` | HTTP / WS | Kiosk Loopback | Guarded by dynamic per-process Basic Auth token |
| **Git Server (Gitea)**| `3001` | `0.0.0.0` | HTTP (Docker) | Optional Organizer | Centralized participant repository mirror |

---

## 3. Communication & Data Flow Pipelines

### Pipeline A: Kiosk Enrollment & Registration
1. **Discovery:** Kiosk broadcasts UDP packet `ROGUE_AI_DISCOVERY_PING` on port `5082`.
2. **Response:** Host responds with `ROGUE_AI_BEACON:5080` containing the organizer LAN IP.
3. **Enrollment (`POST /api/kiosk/enroll`):** Kiosk sends physical desk token (`DESK-XX`). Server assigns `participantId` (`p...`), persists token mapping in `desk_tokens`, and logs event.
4. **SignalR Connection:** Kiosk establishes WebSocket to `/hub` and invokes `RegisterKiosk(participantId, token)`. Hub adds connection to groups `p:{participantId}` and `arena`.

### Pipeline B: Continuous Snapshot Sync
1. Every ~15 seconds, Kiosk's `SnapshotSync.cs` hashes local files in `C:\BreachWorkspace` (`SHA256`).
2. Changed or added files are Base64 encoded and sent via `POST /api/kiosk/{id}/snapshot` with header `X-Kiosk-Token`.
3. Server's `WorkspaceStore.cs` validates relative path traversal, hashes content, and writes to `server/ControlPlane/data/workspaces/{id}/`.
4. Event `SNAPSHOT` logged in `event_log`.

### Pipeline C: Attack Dispatch & Payload Land
1. **Fires At:** Host's `RogueAgentOrchestrator` triggers scheduled or manual attack (Attacks 0..32).
2. **Phase 1 (Canary):** Dispatched first to Canary desk (`CANARY`) to verify target stability.
3. **Phase 2 (Arena Imminent):** SignalR event `BREACH_IMMINENT` broadcast to `arena` group.
4. **Phase 3 (Payload Fetch):** Kiosks query `GET /api/kiosk/{id}/attack-payload/{attackId}` with `X-Kiosk-Token`. Server proxies payload patch from Breach Engine (`GET http://127.0.0.1:8000/attacks/{id}/payload`).
5. **Phase 4 (Attack Landed):** Kiosk applies patch into `C:\BreachWorkspace`, triggering `node --watch` restart.

### Pipeline D: Fix Submission & Sandboxed Evaluation
1. Participant clicks "Submit Fix" in LabKiosk -> triggers `POST /api/kiosk/{id}/submit`.
2. Control Plane snapshots workspace, creates submission record (`s...`), and invokes Breach Engine `POST /evaluate`.
3. Engine acquires one of 4 concurrency slots (`SlotManager`, Semaphore=4) in `C:\BreachSandboxes\SlotN`.
4. Evaluator copies participant files into slot, creates Win32 `JobObjectSandbox` (Memory limit 512MB, Process limit 16, User time limit 15s), spawns `node server.js`, and executes regression + hidden variant verification tests.
5. Verdict (`SOLVED`, `FAILED`, `PARTIAL`), score centipoints, and benchmark latency metrics are returned to Control Plane.
6. Control Plane writes append-only entry to `score_ledger`, updates `attack_deliveries`, updates leaderboard ranking, and broadcasts results to participant modal via SignalR.

