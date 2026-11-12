# Graph Report - rogue-ai  (2026-11-11)

## Corpus Check
- 217 files · ~95,242 words
- Verdict: corpus is large enough that graph structure adds value.
- Unclassified: 39 file(s) not represented in the graph (top: (none) 12, .patch 12, .bat 4)

## Summary
- 3038 nodes · 5412 edges · 168 communities (96 shown, 72 thin omitted)
- Extraction: 94% EXTRACTED · 6% INFERRED · 0% AMBIGUOUS · INFERRED: 349 edges (avg confidence: 0.85)
- Token cost: 37,715 input · 1,759 output

## Community Hubs (Navigation)
- Terminal Core Logic
- Windows System Interop
- Benchmarking and Metrics
- Callback Lifecycle Management
- Security Tier Checks
- Accessibility and Rendering
- Vulnerable Web Routes
- Terminal Event Handling
- Main UI Components
- Attack Manifest Schema
- Session Fracture Manifest
- Web Server Middleware
- WebView and Database Integration
- Attack Execution Manager
- Connection Utility Classes
- Supply Chain Manifest
- Warmup Exercise Manifest
- Reflected XSS Manifest
- Stored XSS Manifest
- SQL Injection Manifest
- CSRF Manifest
- IDOR Manifest
- CPU Sabotage Manifest
- Control Plane Services
- Code Viewer Control
- API Evaluation Models
- Terminal Escape Handlers
- Main Window Logic
- Terminal Color Management
- Database Operations
- Sandbox Task Evaluator
- API Client Services
- Terminal Buffer Management
- Mass Assignment Manifest
- Path Traversal Manifest
- Mouse Selection Logic
- Cookie Auth Manifest
- Terminal Options Configuration
- Attack Metadata Model
- Terminal Parser Logic
- Cell Memory Management
- Terminal Parameter Handling
- Network Retry Logic
- Attack Patching Utility
- SignalR Hub Proxies
- Terminal Cursor Control
- Incident Reporting UI
- Event State Management
- Kiosk System Services
- File Snapshot Management
- Attack Manifest Loader
- Environment Detection Utilities
- Cryptographic Key Generation
- Host Discovery Service
- UI View Controllers
- Admin Route Authorization
- Application Lifecycle Management
- Cookie and Logging Utilities
- Backup and Pruning
- Admin Control Panel
- Kiosk Configuration Settings
- Node.js App Initialization
- Attack Scoring Engine
- Message Transport Layer
- Event Schema Definition
- Ranking and Scheduling
- Onboarding UI Components
- Admin Dashboard Scripts
- Node Package Configuration
- Process Launch Resolver
- Admin Authentication Middleware
- Docker Infrastructure
- SignalR Breach Hub
- Attack Dispatch State
- Reference Package Config
- Socket Connection Lifecycle
- Onboarding Flow Logic
- Host Supervisor Service
- Site Package Config
- Budget and Telemetry
- Session Auth Logic
- Comments Route Implementation
- API and Reporting Routes
- Hyperlink Interaction Logic
- Buffer Reflow Logic
- Project Dependency Management
- Protocol Message Parsing
- Corrupted Auth Library
- GitHub Auth Integration
- Admin Login Endpoints
- Decoration and CSI Handlers
- GitHub Auth UI
- Hub Connection Builder
- Internal Utility Classes
- Payload Auth Library
- Cookie Authentication Logic
- Corrupted Session Auth
- Session Fracture Payload
- Reference Session Auth
- SignalR Client Payloads
- Scroll Handling Logic
- Site Hosting Service
- Observable Stream Management
- Project Documentation Overview
- Server Configuration UI
- Windows Process Management
- SignalR Connection Management
- Cell Styling and Selection
- Cell Decoration Management
- Canvas Rendering Logic
- Project Dependencies and Frameworks
- Sound Effects Player
- Stored XSS Hints
- CSRF Hints
- IDOR Hints
- CPU Sabotage Hints
- Supply Chain Hints
- Regex and Quota Monitoring
- Terminal Fit Addon
- Terminal Window Control
- Onboarding Login Buttons
- Server Entry Point
- JavaScript Complexity Analysis
- Link Registration Logic
- Input Composition Handling
- Decoration Element Rendering
- Evaluation Verdict Models
- System Design Documentation
- Corrupted HTML Templates
- Payload HTML Templates
- Reference HTML Templates
- Compliance Checkbox UI
- Warmup Challenge Hints
- Reflected XSS Hints
- SQL Injection Hints
- Session Fracture Hints
- Composition Update Logic
- Admin Access Interface
- Event Management Views
- Attack Resource Packs
- IT Procurement Documents
- Security Chain Fingerprint
- Project Decision Log
- Operational Runbook
- Docker Infrastructure Config
- Software Download Page
- Kiosk Simulation Tool
- AEGIS Project Site

## God Nodes (most connected - your core abstractions)
1. `E` - 105 edges
2. `d` - 60 edges
3. `constructor()` - 57 edges
4. `MainWindow` - 56 edges
5. `P` - 53 edges
6. `q` - 46 edges
7. `i()` - 42 edges
8. `Database` - 40 edges
9. `c()` - 39 edges
10. `S` - 38 edges

## Surprising Connections (you probably didn't know these)
- `handleBlur()` --indirect_call--> `m()`  [INFERRED]
  kiosk/LabKiosk/Assets/terminal/xterm.js → server/ControlPlane/wwwroot/signalr.min.js
- `handleFocus()` --indirect_call--> `m()`  [INFERRED]
  kiosk/LabKiosk/Assets/terminal/xterm.js → server/ControlPlane/wwwroot/signalr.min.js
- `Engine Requirements` --references--> `Breach Evaluation Engine`  [INFERRED]
  engine/requirements.txt → AGENTS.md
- `Terminal UI` --implements--> `WPF LabKiosk`  [INFERRED]
  kiosk/LabKiosk/Assets/terminal/terminal.html → AGENTS.md
- `Final Build Plan` --references--> `OpenAPI/Schema Contracts`  [EXTRACTED]
  docs/build-plan.md → AGENTS.md

## Import Cycles
- None detected.

## Hyperedges (group relationships)
- **Rogue AI System Architecture** — server_entity, engine_entity, kiosk_entity, site_entity [EXTRACTED 1.00]
- **Attack Lifecycle Flow** — server_entity, attacks_entity, kiosk_entity, engine_entity [EXTRACTED 0.95]
- **Admin Interface Suite** — server_controlplane_wwwroot_index_html, server_controlplane_wwwroot_admin_index_html, server_controlplane_wwwroot_projector_html [INFERRED 0.85]

## Communities (168 total, 72 thin omitted)

### Community 0 - "Terminal Core Logic"
Cohesion: 0.02
Nodes (39): addDecoration(), addEscHandler(), _addLineToZone(), addMarker(), addOscHandler(), areSelectionValuesReversed(), clearListeners(), _createSelectionElement() (+31 more)

### Community 1 - "Windows System Interop"
Cohesion: 0.07
Nodes (13): CREDENTIAL, CredentialManager, ConPtyNative, Coord, ProcessInformation, StartupInfo, StartupInfoEx, ConPtySession (+5 more)

### Community 2 - "Benchmarking and Metrics"
Cohesion: 0.07
Nodes (15): bootstrap_ci(), compute_composite_score(), derive_references_from_pilot(), mann_whitney_u_test(), normalize_metric(), synthesize_naive(), analyze_directory(), analyze_file() (+7 more)

### Community 3 - "Callback Lifecycle Management"
Cohesion: 0.06
Nodes (5): _cancelCallback(), n(), o(), r(), _requestCallback()

### Community 4 - "Security Tier Checks"
Cohesion: 0.07
Nodes (22): tier_checks(), _get_comments(), _post_comment(), tier_checks(), _search(), tier_checks(), _login(), _post_json() (+14 more)

### Community 5 - "Accessibility and Rendering"
Cohesion: 0.06
Nodes (50): addEncoding(), addProtocol(), _announceCharacters(), _clearLiveRegion(), clearRange(), clearTextureAtlas(), constructor(), _createAccessibilityTreeNode() (+42 more)

### Community 6 - "Vulnerable Web Routes"
Cohesion: 0.07
Nodes (24): express, express, express, express, express, express, express, express (+16 more)

### Community 8 - "Main UI Components"
Cohesion: 0.06
Nodes (44): IsDropDownOpen, AdminButton, AgyMeter, AttackBadgesPanel, bd, border, BtnGoogleAccount, BtnProfile (+36 more)

### Community 9 - "Attack Manifest Schema"
Cohesion: 0.05
Nodes (45): pattern, type, type, items, type, maximum, minimum, type (+37 more)

### Community 10 - "Session Fracture Manifest"
Cohesion: 0.05
Nodes (41): band, codename, dispatch, imminent_seconds, mode, time, fires_at, fix_requires (+33 more)

### Community 11 - "Web Server Middleware"
Cohesion: 0.07
Nodes (27): compile(), express(), fail(), fs, http, makeStack(), MIME, path (+19 more)

### Community 12 - "WebView and Database Integration"
Cohesion: 0.16
Nodes (7): WebViewEnv, SystemClock, Now, UtcNow, RogueAgentOrchestrator, Attacks, RogueAgentOrchestratorTests

### Community 13 - "Attack Execution Manager"
Cohesion: 0.07
Nodes (8): check_compliance_fast(), _deep_worker_loop(), evaluate_attack(), evaluate_fast(), from_manifest_load_all(), SlotManager, TestEvaluator, TestSandboxEscape

### Community 15 - "Supply Chain Manifest"
Cohesion: 0.05
Nodes (39): band, codename, dispatch, imminent_seconds, mode, time, fires_at, fix_requires (+31 more)

### Community 16 - "Warmup Exercise Manifest"
Cohesion: 0.05
Nodes (38): band, codename, dispatch, imminent_seconds, mode, time, fires_at, fix_requires (+30 more)

### Community 17 - "Reflected XSS Manifest"
Cohesion: 0.05
Nodes (38): band, codename, dispatch, imminent_seconds, mode, time, fires_at, fix_requires (+30 more)

### Community 18 - "Stored XSS Manifest"
Cohesion: 0.05
Nodes (38): band, codename, dispatch, imminent_seconds, mode, time, fires_at, fix_requires (+30 more)

### Community 19 - "SQL Injection Manifest"
Cohesion: 0.05
Nodes (38): band, codename, dispatch, imminent_seconds, mode, time, fires_at, fix_requires (+30 more)

### Community 20 - "CSRF Manifest"
Cohesion: 0.05
Nodes (38): band, codename, dispatch, imminent_seconds, mode, time, fires_at, fix_requires (+30 more)

### Community 21 - "IDOR Manifest"
Cohesion: 0.05
Nodes (38): band, codename, dispatch, imminent_seconds, mode, time, fires_at, fix_requires (+30 more)

### Community 22 - "CPU Sabotage Manifest"
Cohesion: 0.05
Nodes (38): band, codename, dispatch, imminent_seconds, mode, time, fires_at, fix_requires (+30 more)

### Community 23 - "Control Plane Services"
Cohesion: 0.11
Nodes (9): ControlPlane.Hubs, ControlPlane.Services, ControlPlane.Data, ControlPlane.Tests, ControlPlane.Models, HubEvents, BroadcastRequest, EnrollRequest (+1 more)

### Community 24 - "Code Viewer Control"
Cohesion: 0.09
Nodes (10): Editor, FileLabel, SaveButton, Tree, UserControl, CodeViewer, Button, TextBlock (+2 more)

### Community 25 - "API Evaluation Models"
Cohesion: 0.10
Nodes (15): apply(), ApplyRequest, attacks(), EvaluateRequest, get_attack_payload(), get_evaluation(), health(), resolve_workspace() (+7 more)

### Community 27 - "Main Window Logic"
Cohesion: 0.14
Nodes (3): AttackMeta, MainWindow, WorkspaceSync

### Community 29 - "Database Operations"
Cohesion: 0.13
Nodes (3): Database, PayloadGatingAndDeskTokenTests, SecurityHardeningTests

### Community 30 - "Sandbox Task Evaluator"
Cohesion: 0.08
Nodes (14): call_check_fn(), DeepEvalTask, enqueue_deep_evaluation(), ensure_node_modules(), evaluate(), find_free_port(), IO_COUNTERS, JOBOBJECT_BASIC_LIMIT_INFORMATION (+6 more)

### Community 31 - "API Client Services"
Cohesion: 0.08
Nodes (9): ApiClient, Token, FetchResponse, PayloadResponse, RegisterResponse, RemoteFile, SnapshotFileDto, SnapshotSync (+1 more)

### Community 33 - "Mass Assignment Manifest"
Cohesion: 0.06
Nodes (30): band, codename, dispatch, imminent_seconds, mode, time, fires_at, fix_requires (+22 more)

### Community 34 - "Path Traversal Manifest"
Cohesion: 0.06
Nodes (30): band, codename, dispatch, imminent_seconds, mode, time, fires_at, fix_requires (+22 more)

### Community 35 - "Mouse Selection Logic"
Cohesion: 0.09
Nodes (30): _addMouseDownListeners(), clearSelection(), _convertViewportColToCharacterIndex(), _dragScroll(), _fireEventIfSelectionChanged(), _fireOnSelectionChange(), _getMouseBufferCoords(), _getMouseEventScrollAmount() (+22 more)

### Community 36 - "Cookie Auth Manifest"
Cohesion: 0.07
Nodes (29): band, codename, dispatch, imminent_seconds, mode, time, fires_at, fix_requires (+21 more)

### Community 37 - "Terminal Options Configuration"
Cohesion: 0.08
Nodes (20): a(), addCsiHandler(), addDcsHandler(), addRefreshCallback(), delete(), end(), forEachByKey(), getKeyIterator() (+12 more)

### Community 38 - "Attack Metadata Model"
Cohesion: 0.07
Nodes (26): AttackDispatch, ImminentSeconds, Mode, Time, AttackHints, Path, Tiers, AttackNarrative (+18 more)

### Community 42 - "Network Retry Logic"
Cohesion: 0.10
Nodes (5): b(), i, j, X, z

### Community 43 - "Attack Patching Utility"
Cohesion: 0.17
Nodes (9): make_patch(), read(), sub(), already_applied(), apply_attack(), ensure_git_repo(), git(), make_repo() (+1 more)

### Community 44 - "SignalR Hub Proxies"
Cohesion: 0.22
Nodes (7): DummyClientProxy, DummyHubClients, All, RecordingClientProxy, RecordingHubClients, All, SentMessages

### Community 46 - "Incident Reporting UI"
Cohesion: 0.11
Nodes (13): b, BtnInvestigate, CardBorder, TxtClassification, TxtSymptoms, TxtTelemetry, TxtTitle, Window (+5 more)

### Community 47 - "Event State Management"
Cohesion: 0.13
Nodes (5): EventState, Frozen, Paused, PausedAt, EventFreezeTests

### Community 49 - "File Snapshot Management"
Cohesion: 0.17
Nodes (5): FetchRequest, FileEntry, SnapshotFile, SnapshotRequest, WorkspaceStore

### Community 50 - "Attack Manifest Loader"
Cohesion: 0.09
Nodes (20): AttackManifest, Band, Codename, Dispatch, FiresAt, Hints, Id, Name (+12 more)

### Community 51 - "Environment Detection Utilities"
Cohesion: 0.11
Nodes (3): d, g, o

### Community 52 - "Cryptographic Key Generation"
Cohesion: 0.16
Nodes (8): sha256_file(), ensure_master_key(), export_desk_slips(), init_genesis_block(), call(), kiosk_worker(), load_desk_tokens(), main()

### Community 53 - "Host Discovery Service"
Cohesion: 0.12
Nodes (3): HostDiscoveryClient, AttackDispatcher, HostDiscoveryBroadcaster

### Community 54 - "UI View Controllers"
Cohesion: 0.19
Nodes (3): LabKiosk.Controls, LabKiosk, LabKiosk.Terminal

### Community 55 - "Admin Route Authorization"
Cohesion: 0.19
Nodes (12): esc(), layout(), requireAuth(), requireRole(), express, express, express, express (+4 more)

### Community 56 - "Application Lifecycle Management"
Cohesion: 0.13
Nodes (3): Application, App, WorkspaceBackup

### Community 57 - "Cookie and Logging Utilities"
Cohesion: 0.10
Nodes (7): _, a, c, f, h, T, w

### Community 58 - "Backup and Pruning"
Cohesion: 0.15
Nodes (4): BackupPruneResult, BackupService, BackupStatus, BackupRetentionTests

### Community 59 - "Admin Control Panel"
Cohesion: 0.14
Nodes (11): CopyKey, OpenKiosk, OpenProjector, StatusText, Web, Window, AdminWindow, Button (+3 more)

### Community 61 - "Kiosk Configuration Settings"
Cohesion: 0.10
Nodes (20): KioskSettings, AgyPath, DataPath, DeskId, DeskToken, DisplayName, GitHubHandle, GoogleAccount (+12 more)

### Community 62 - "Node.js App Initialization"
Cohesion: 0.13
Nodes (12): createApp(), express, path, hashPassword(), { DatabaseSync }, open(), path, seed() (+4 more)

### Community 63 - "Attack Scoring Engine"
Cohesion: 0.15
Nodes (7): clamp(), failed_submission_penalty(), performance_points(), prompt_efficiency_points(), speed_points(), survival_bonus(), ScoringTests

### Community 65 - "Event Schema Definition"
Cohesion: 0.11
Nodes (17): type, type, type, properties, attack_id, participant_id, payload, ts (+9 more)

### Community 66 - "Ranking and Scheduling"
Cohesion: 0.18
Nodes (6): IClock, Now, UtcNow, Ranking, Standing, RankingTests

### Community 67 - "Onboarding UI Components"
Cohesion: 0.16
Nodes (15): b, BadgeGithub, BadgeGoogle, TxtDisplayName, TxtGithubHandle, TxtGithubStatus, TxtGoogleEmail, TxtGoogleStatus (+7 more)

### Community 68 - "Admin Dashboard Scripts"
Cohesion: 0.35
Nodes (16): api(), checkEventStatus(), confirmCanary(), escapeHtml(), eventStartTime, fireArena(), fireBossAttack(), fireCanary() (+8 more)

### Community 69 - "Node Package Configuration"
Cohesion: 0.13
Nodes (16): dependencies, express@4.21.1, rogue-logger@1.0.0, description, engines, node, express, name (+8 more)

### Community 70 - "Process Launch Resolver"
Cohesion: 0.18
Nodes (6): ExeResolver, OpenCodeHost, ConfigPath, Environment, Password, Url

### Community 72 - "Docker Infrastructure"
Cohesion: 0.12
Nodes (16): context, dockerfile, build, ports, restart, volumes, environment, image (+8 more)

### Community 74 - "SignalR Breach Hub"
Cohesion: 0.14
Nodes (9): BreachHub, DummyHubContext, Clients, Groups, RecordingHubContext, Clients, Groups, RecordingClients (+1 more)

### Community 75 - "Attack Dispatch State"
Cohesion: 0.13
Nodes (14): AttackDispatchStatus, AttackId, Codename, DispatchedAt, Name, ScheduledTime, State, DispatchState (+6 more)

### Community 76 - "Reference Package Config"
Cohesion: 0.14
Nodes (14): dependencies, express@4.21.1, description, engines, node, express, name, private (+6 more)

### Community 77 - "Socket Connection Lifecycle"
Cohesion: 0.19
Nodes (6): handleBlur(), handleFocus(), I(), m(), n, v

### Community 80 - "Host Supervisor Service"
Cohesion: 0.22
Nodes (4): HostSupervisor, AdminKey, EngineUrl, ServerUrl

### Community 81 - "Site Package Config"
Cohesion: 0.14
Nodes (14): dependencies, express@4.21.1, description, engines, node, express, name, private (+6 more)

### Community 83 - "Budget and Telemetry"
Cohesion: 0.18
Nodes (6): BudgetState, AgyExhausted, BothSpent, OpenCodeCap, OpenCodeCapReached, OpenCodePrompts

### Community 84 - "Session Auth Logic"
Cohesion: 0.23
Nodes (10): crypto, endSession(), loadUser(), parseCookies(), requireRole(), sessions, startSession(), verifyPassword() (+2 more)

### Community 85 - "Comments Route Implementation"
Cohesion: 0.14
Nodes (9): comments, express, router, comments, express, router, app, express (+1 more)

### Community 86 - "API and Reporting Routes"
Cohesion: 0.17
Nodes (8): crypto, express, crypto, express, router, crypto, express, router

### Community 87 - "Hyperlink Interaction Logic"
Cohesion: 0.26
Nodes (12): _askForLink(), attachToDom(), _checkLinkProviderResult(), _clearCurrentLink(), _createLinkUnderlineEvent(), _fireUnderlineEvent(), _handleHover(), _handleNewLink() (+4 more)

### Community 88 - "Buffer Reflow Logic"
Cohesion: 0.23
Nodes (12): _batchedMemoryCleanup(), clear(), fillViewportRows(), _fireOnCanvasResize(), getBlankLine(), _getCorrectBufferLength(), getNullCell(), _reflow() (+4 more)

### Community 90 - "Project Dependency Management"
Cohesion: 0.17
Nodes (10): BCrypt.Net-Next (4.0.3), Microsoft.Data.Sqlite (9.0.2), Microsoft.NET.Test.Sdk (17.11.1), xunit (2.9.2), xunit.runner.visualstudio (2.8.2), YamlDotNet (16.3.0), Microsoft.NET.Sdk.Web, net9.0 (+2 more)

### Community 92 - "Corrupted Auth Library"
Cohesion: 0.22
Nodes (5): crypto, endSession(), loadUser(), parseCookies(), sessions

### Community 94 - "Admin Login Endpoints"
Cohesion: 0.20
Nodes (3): ControlPlane.Auth, AdminLoginEndpoints, LoginBody

### Community 96 - "GitHub Auth UI"
Cohesion: 0.22
Nodes (9): AuthBrowser, b, LoadingOverlay, TxtLoading, TxtStatus, Window, Border, TextBlock (+1 more)

### Community 99 - "Payload Auth Library"
Cohesion: 0.24
Nodes (5): crypto, endSession(), loadUser(), parseCookies(), sessions

### Community 100 - "Cookie Authentication Logic"
Cohesion: 0.24
Nodes (5): crypto, endSession(), loadUser(), parseCookies(), sessions

### Community 101 - "Corrupted Session Auth"
Cohesion: 0.24
Nodes (5): crypto, endSession(), loadUser(), parseCookies(), sessions

### Community 102 - "Session Fracture Payload"
Cohesion: 0.24
Nodes (5): crypto, endSession(), loadUser(), parseCookies(), sessions

### Community 103 - "Reference Session Auth"
Cohesion: 0.24
Nodes (5): crypto, endSession(), loadUser(), parseCookies(), sessions

### Community 104 - "SignalR Client Payloads"
Cohesion: 0.22
Nodes (3): AttackLandedPayload, BreachImminentPayload, TelemetryRequest

### Community 105 - "Scroll Handling Logic"
Cohesion: 0.29
Nodes (10): _applyScrollModifier(), _bubbleScroll(), _clearSmoothScrollState(), getLinesScrolled(), _getPixelsScrolled(), handleTouchMove(), handleWheel(), scrollLines() (+2 more)

### Community 109 - "Project Documentation Overview"
Cohesion: 0.33
Nodes (5): Breach Evaluation Engine, Engine Requirements, WPF LabKiosk, Terminal UI, ASP.NET Core Control Plane

### Community 113 - "Cell Styling and Selection"
Cohesion: 0.33
Nodes (7): _addStyle(), _applyMinimumContrast(), _areCoordsInSelection(), createRow(), _getContrastCache(), _isCellInSelection(), v()

### Community 116 - "Canvas Rendering Logic"
Cohesion: 0.33
Nodes (7): _refreshCanvasDimensions(), _refreshColorZonePadding(), _refreshDecorations(), _refreshDrawConstants(), _refreshDrawHeightConstants(), _registerBufferChangeListeners(), _renderColorZone()

### Community 117 - "Project Dependencies and Frameworks"
Cohesion: 0.33
Nodes (5): net9.0-windows, Microsoft.NET.Sdk, AvalonEdit (6.3.1.120), Microsoft.AspNetCore.SignalR.Client (9.0.2), Microsoft.Web.WebView2 (1.0.2903.40)

### Community 119 - "Stored XSS Hints"
Cohesion: 0.40
Nodes (4): tiers, cost_centipoints, level, text

### Community 120 - "CSRF Hints"
Cohesion: 0.40
Nodes (4): tiers, cost_centipoints, level, text

### Community 121 - "IDOR Hints"
Cohesion: 0.40
Nodes (4): tiers, cost_centipoints, level, text

### Community 122 - "CPU Sabotage Hints"
Cohesion: 0.40
Nodes (4): tiers, cost_centipoints, level, text

### Community 123 - "Supply Chain Hints"
Cohesion: 0.40
Nodes (4): tiers, cost_centipoints, level, text

### Community 128 - "Onboarding Login Buttons"
Cohesion: 0.40
Nodes (4): BtnAutoDetectGithub, BtnGithubDevice, BtnGoogleLogin, Button

### Community 131 - "Link Registration Logic"
Cohesion: 0.67
Nodes (4): addLineToLink(), _getEntryIdKey(), registerLink(), _removeMarkerFromLink()

### Community 132 - "Input Composition Handling"
Cohesion: 0.50
Nodes (4): compositionend(), _finalizeComposition(), _handleAnyTextareaChanges(), keydown()

### Community 133 - "Decoration Element Rendering"
Cohesion: 0.67
Nodes (4): _createElement(), _refreshStyle(), _refreshXPosition(), _renderDecoration()

## Knowledge Gaps
- **150 isolated node(s):** `TextBlock`, `WebView2`, `TreeView`, `Button`, `TextBlock` (+145 more)
  These have ≤1 connection - possible missing edges or undocumented components. (Counts symbols only; 1213 node(s) total have ≤1 connection when file, concept and rationale nodes are included.)
- **72 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `MainWindow` connect `Main Window Logic` to `Process Launch Resolver`, `Main UI Components`, `Site Hosting Service`, `Server Configuration UI`, `Incident Reporting UI`, `Onboarding Flow Logic`, `SignalR Connection Management`, `Budget and Telemetry`, `Kiosk Configuration Settings`, `Regex and Quota Monitoring`, `UI View Controllers`, `Application Lifecycle Management`, `Code Viewer Control`, `Admin Control Panel`, `GitHub Auth Integration`, `API Client Services`?**
  _High betweenness centrality (0.037) - this node is a cross-community bridge._
- **Are the 4 inferred relationships involving `constructor()` (e.g. with `f()` and `t()`) actually correct?**
  _`constructor()` has 4 INFERRED edges - model-reasoned connections that need verification._
- **What connects `TextBlock`, `WebView2`, `TreeView` to the rest of the system?**
  _150 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Terminal Core Logic` be split into smaller, more focused modules?**
  _Cohesion score 0.020225039168209658 - nodes in this community are weakly interconnected._
- **Why does `SnapshotSync` connect `API Client Services` to `Ranking and Scheduling`, `Site Hosting Service`, `Server Configuration UI`, `Kiosk System Services`, `Application Lifecycle Management`, `Backup and Pruning`, `Main Window Logic`?**
  _High betweenness centrality (0.033) - this node is a cross-community bridge._
- **Are the 2 inferred relationships involving `MainWindow` (e.g. with `AdminWindow.OnOpenKiosk()` and `App.OnStartup()`) actually correct?**
  _`MainWindow` has 2 INFERRED edges - model-reasoned connections that need verification._
- **Should `Windows System Interop` be split into smaller, more focused modules?**
  _Cohesion score 0.06746031746031746 - nodes in this community are weakly interconnected._