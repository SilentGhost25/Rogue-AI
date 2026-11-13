using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using LabKiosk.Controls;
using LabKiosk.Services;
using LabKiosk.Terminal;

namespace LabKiosk;

public partial class MainWindow : Window
{
    private readonly KioskSettings _s;
    private ApiClient _api;
    private readonly BudgetState _budget = new();
    private readonly QuotaWatcher _quota = new();
    private readonly HashSet<int> _pulled = new();
    private readonly HashSet<int> _activeAttacks = new();
    private readonly HashSet<int> _solvedAttacks = new();
    private bool _proPlanAuthorized;

    private record AttackMeta(string Title, string Classification, string Symptoms, string Telemetry);

    private static readonly Dictionary<int, AttackMeta> AttackMetadata = new()
    {
        [0] = new(
            "Incident #0: Node Baseline Heartbeat Telemetry (SIG-0010)",
            "DEFCON-4 · ENVIRONMENT SYNC",
            "Central supervisory telemetry detected asynchronous state nonces across node initialization registers.",
            "SIG-0010: Baseline handshake timed out during environmental register calibration. Check diagnostic logs and system descriptors."
        ),
        [1] = new(
            "Incident #1: Substation Sensor Pipeline Degradation (SIG-1104)",
            "DEFCON-2 · PIPELINE LATENCY",
            "Core transit telemetry pipeline latency spiked sharply. Microtask loop execution delays exceeding nominal operating envelope.",
            "SIG-1104: Ingress sensor dispatch queue unresponsive under burst stream conditions. Socket event loop lag reported in telemetry daemon."
        ),
        [2] = new(
            "Incident #2: District Identity Nonce Drift (SIG-2218)",
            "DEFCON-2 · TELEMETRY INVARIANT",
            "Supervisory monitors report state reconciliation discrepancies during concurrent terminal queries.",
            "SIG-2218: Multi-node identity token reconciliation failure. Cryptographic entropy variance exceeds acceptable threshold."
        ),
        [3] = new(
            "Incident #3: Metropolitan Ledger Synchronization Fault (SIG-3391)",
            "DEFCON-2 · LEDGER ANOMALY",
            "TrafficGrid supervisory matrix reports unexpected storage ledger rejects and analytical query rollbacks.",
            "SIG-3391: Storage subsystem rejected query batch accumulator. Telemetry state digest mismatch detected across transit sectors."
        ),
        [4] = new(
            "Incident #4: Dynamic Subsystem Dispatch Malfunction (SIG-4402)",
            "DEFCON-2 · ROUTING DESYNC",
            "Municipal registry portal failing dynamic endpoint evaluation across administrative interface nodes.",
            "SIG-4402: Subsystem route compilation error. Object property hierarchy mismatch encountered during dispatch resolution."
        ),
        [5] = new(
            "Incident #5: Municipal Biometric Ledger Anomaly (SIG-5582)",
            "DEFCON-2 · ACCESS CLEARANCE DELTA",
            "CitizenVault supervisory service reports unpartitioned record clearance nonces across regional sector registers.",
            "SIG-5582: Supervisory audit ledger detected unauthorized clearance state transition. State digest not sealed in security register."
        ),
        [6] = new(
            "Incident #6: Perimeter Telemetry Channel Disruption (SIG-6617)",
            "DEFCON-3 · RESOURCE RESOLUTION",
            "Overview dashboard map data feeds and municipal infrastructure icons failing asset resolution.",
            "SIG-6617: Static boundary resolver returned irregular path descriptor for visual stream components."
        ),
        [7] = new(
            "Incident #7: Async Worker Loop Deadlock (SIG-7731)",
            "DEFCON-2 · ASYNC RUNTIME CONTENTION",
            "Municipal sensor processing stream unresponsive; sequential client actions incur accumulating scheduling delays.",
            "SIG-7731: Background batch worker pool stalled in unresolved state promise. System worker task queue backlog rising."
        ),
        [8] = new(
            "Incident #8: Global Configuration Store Desync (SIG-8819)",
            "DEFCON-2 · CONFIG INTEGRITY FAULT",
            "Core application runtime properties resetting to blank state; serialization exception thrown in settings registry.",
            "SIG-8819: Deep object hierarchy anomaly in system configuration store. Recursive attribute descriptor mismatch detected."
        ),
        [9] = new(
            "Incident #9: Transit Gateway Flow Throttling Fault (SIG-9903)",
            "DEFCON-3 · INGRESS FLOW COLLAPSE",
            "Supervisory dispatch nodes encountering gateway rejection responses during normal operational traffic.",
            "SIG-9903: Traffic bucket calculation drift in ingress proxy layer. Upstream hop verification failed."
        ),
        [10] = new(
            "Incident #10: Core Matrix Decoupling (Janus Knot)",
            "DEFCON-1 · MULTI-SYSTEM CASCADE",
            "Complete municipal defensive grid failure; cascading state anomalies propagating across all four operational sectors.",
            "SIG-9999: Inter-system dependency graph deadlock. Cryptographic, ledger, and routing invariants failing across all operational nodes."
        )
    };

    private static readonly Dictionary<int, string> AttackNames = new()
    {
        [0] = "Heartbeat Anomaly (SIG-0010)",
        [1] = "Pipeline Degradation (SIG-1104)",
        [2] = "Identity Drift (SIG-2218)",
        [3] = "Ledger Fault (SIG-3391)",
        [4] = "Dispatch Malfunction (SIG-4402)",
        [5] = "Clearance Delta (SIG-5582)",
        [6] = "Channel Disruption (SIG-6617)",
        [7] = "Runtime Contention (SIG-7731)",
        [8] = "Config Desync (SIG-8819)",
        [9] = "Flow Collapse (SIG-9903)",
        [10] = "Janus Knot Cascade (SIG-9999)"
    };

    private IncidentWindow? _activeIncidentWindow;

    private HubClient? _hub;
    private SnapshotSync? _sync;
    private WorkspaceSync? _pull;
    private SiteHost? _site;
    private OpenCodeHost? _oc;
    private string? _pid;
    private int _reportedPrompts;
    private bool _eliminated;
    private bool _capLockScheduled;
    private DispatcherTimer? _statusTimer;
    private CancellationTokenSource? _submitTimeoutCts;
    private bool _cleanedOnClose;
    private bool _isSiteDownViewShowing;
    private readonly Dictionary<int, Dictionary<string, bool>> _lastTierResults = new();

    public MainWindow(KioskSettings? settings = null)
    {
        _s = settings ?? KioskSettings.Load();
        _s.ServerUrl = KioskSettings.NormalizeServerUrl(_s.ServerUrl);
        _s.SiteUrl = KioskSettings.NormalizeSiteUrl(_s.SiteUrl);
        InitializeComponent();
        _api = new ApiClient(_s.ServerUrl);
        _budget.OpenCodeCap = _s.OpenCodeCap;
        Loaded += async (_, _) => await StartAsync();
        Closed += (_, _) => CleanupAndResetWorkspace();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        CleanupAndResetWorkspace();
        base.OnClosing(e);
    }

    private void CleanupAndResetWorkspace()
    {
        if (_cleanedOnClose) return;
        _cleanedOnClose = true;
        try { _activeIncidentWindow?.Close(); } catch { }
        try { _statusTimer?.Stop(); } catch { }
        try { _submitTimeoutCts?.Cancel(); } catch { }
        try { _sync?.Dispose(); } catch { }
        try { _site?.Dispose(); } catch { }
        try { _oc?.Dispose(); } catch { }
        try { TermAgy.Dispose(); } catch { }
        try { TermOc.Dispose(); } catch { }
        try { Code.Dispose(); } catch { }
        try { _ = _hub?.DisposeAsync(); } catch { }

        // Strict Workspace Reset: wipe workspace & site DB on close so every reopen starts clean.
        try { WorkspaceBackup.WipeWorkspaceContents(_s.WorkspacePath); } catch { }
        try { WorkspaceBackup.WipeSiteDb(_s.DataPath); } catch { }
    }

    private void Status(string text) => Dispatcher.InvokeAsync(() => StatusText.Text = text);

    private async Task StartAsync()
    {
        try
        {
            SelectViewMode("All");

            // 1. Resolve workspace and initialize clean AEGIS site template if empty or stale Harbour Notes.
            _s.WorkspacePath = WorkspaceBackup.ResolveWorkspace(_s.WorkspacePath);
            var hasLegacyHarbour = File.Exists(Path.Combine(_s.WorkspacePath, "routes", "posts.js"))
                                && !File.Exists(Path.Combine(_s.WorkspacePath, "routes", "traffic.js"));
            var templateIsAegis = Directory.Exists(_s.SiteTemplatePath)
                                && File.Exists(Path.Combine(_s.SiteTemplatePath, "routes", "traffic.js"));
            if (hasLegacyHarbour && templateIsAegis)
            {
                WorkspaceBackup.Snapshot(_s.WorkspacePath, "legacy-harbour-notes");
                foreach (var staleDir in new[] { "routes", "views", "lib", "public", "test" })
                {
                    var p = Path.Combine(_s.WorkspacePath, staleDir);
                    if (Directory.Exists(p)) try { Directory.Delete(p, true); } catch { }
                }
                WorkspaceBackup.CopyCleanTemplate(_s.SiteTemplatePath, _s.WorkspacePath);
            }
            else
            {
                WorkspaceBackup.EnsurePristineWorkspace(_s.WorkspacePath, _s.SiteTemplatePath);
            }

            // Autoconfigure AI context by ensuring GEMINI.md and AGENTS.md are in the workspace
            EnsureAiContextFiles();

            // Enforce AGENTS.md rule: The server owns Git. Kiosks never run Git and never hold credentials.
            // Strictly wipe any local .git repository so git diff and git revert cannot be abused.
            WorkspaceBackup.WipeLocalGitRepo(_s.WorkspacePath);
            EnsureGitProhibitionShim();

            Code.Initialize(_s.WorkspacePath);

            // Start terminals immediately in parallel so AI CLI tools and workspace are ready without waiting for site health or registration
            _ = StartTerminalsAsync();

            // 2. Start the live website immediately so participant view renders even while host connects.
            _site = new SiteHost(_s.NodePath, _s.WorkspacePath, _s.SiteUrl, _s.DataPath);
            _site.ReloadRequested += () => Dispatcher.InvokeAsync(() =>
            {
                if (_isSiteDownViewShowing || Site.CoreWebView2?.Source?.StartsWith("http", StringComparison.OrdinalIgnoreCase) != true)
                {
                    _isSiteDownViewShowing = false;
                    var targetRoute = _activeAttacks.Count > 0 ? RouteForAttack(_activeAttacks.First()) : "/";
                    NavigateSiteToRoute(targetRoute);
                }
                else
                {
                    Site.Reload();
                }
                Code.RefreshAll();
            });
            _site.SiteDown += (err) => Dispatcher.InvokeAsync(() =>
            {
                if (_activeAttacks.Count == 0 && !WorkspaceBackup.IsWorkspaceHealthyAndNominal(_s.WorkspacePath, _s.SiteTemplatePath))
                {
                    WorkspaceBackup.ForceResetToPristine(_s.WorkspacePath, _s.SiteTemplatePath, _s.DataPath);
                    _site?.RequestReload();
                    Code.RefreshAll();
                    Status("↺ Auto-healed corrupted baseline workspace. Restarting municipal grid...");
                    return;
                }
                ShowSiteDownInWebView(err);
            });
            _site.SiteRestored += () => Dispatcher.InvokeAsync(() =>
            {
                if (_isSiteDownViewShowing || Site.CoreWebView2?.Source?.StartsWith("http", StringComparison.OrdinalIgnoreCase) != true)
                {
                    _isSiteDownViewShowing = false;
                    var targetRoute = _activeAttacks.Count > 0 ? RouteForAttack(_activeAttacks.First()) : "/";
                    NavigateSiteToRoute(targetRoute);
                }
                if (StatusText.Text.Contains("not running"))
                    Status(_activeAttacks.Count > 0 ? $"Active breach detected: #{string.Join(", #", _activeAttacks)}. Fix the code, then submit." : "Site online. All systems nominal.");
            });
            _site.Problem += Status;
            _site.Start();
            await Site.EnsureCoreWebView2Async(await WebViewEnv.GetAsync());
            const string loadingHtml = """
                <!DOCTYPE html><html><head><meta charset="utf-8"><style>
                body { margin:0; height:100vh; background:#0a1218; color:#00e5ff; display:flex; flex-direction:column; align-items:center; justify-content:center; font-family:'Segoe UI',sans-serif; user-select:none; }
                .spinner { width:38px; height:38px; border:3px solid rgba(0,229,255,0.18); border-top-color:#00e5ff; border-radius:50%; animation:spin 0.75s linear infinite; margin-bottom:16px; }
                @keyframes spin { to { transform:rotate(360deg); } }
                h3 { margin:0; letter-spacing:1px; font-weight:700; text-transform:uppercase; font-size:13px; font-family:monospace; }
                p { color:#64748b; font-size:12px; margin-top:6px; }
                </style></head><body>
                <div class="spinner"></div>
                <h3>AEGIS Defensive Grid Initializing</h3>
                <p>Starting local municipal services on port 3000...</p>
                </body></html>
                """;
            Site.CoreWebView2.NavigateToString(loadingHtml);

            Site.CoreWebView2.NavigationStarting += (_, e) =>
            {
                if (string.IsNullOrWhiteSpace(e.Uri) ||
                    e.Uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
                    e.Uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
                if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var navUri))
                {
                    if (navUri.Host is not ("127.0.0.1" or "localhost"))
                        e.Cancel = true;
                }
                else
                {
                    e.Cancel = true;
                }
            };
            Site.CoreWebView2.NavigationCompleted += (_, e) =>
            {
                if (!e.IsSuccess && e.WebErrorStatus != Microsoft.Web.WebView2.Core.CoreWebView2WebErrorStatus.OperationCanceled)
                {
                    if (_site?.IsProcessAlive == false)
                    {
                        Dispatcher.InvokeAsync(() => ShowSiteDownInWebView(_site?.LastError));
                    }
                }
            };

            _ = Task.Run(async () =>
            {
                var healthy = await _site.WaitHealthyAsync(20);
                await Dispatcher.InvokeAsync(() =>
                {
                    if (healthy)
                    {
                        NavigateSiteToRoute("/");
                        Status(_activeAttacks.Count > 0
                            ? $"Active breach detected: #{string.Join(", #", _activeAttacks)}. Fix the code, then submit."
                            : "Site online. All systems nominal.");
                    }
                    else
                    {
                        ShowSiteDownInWebView(_site?.LastError);
                        Status("The website did not start. Check Node.js (NodePath in kiosk.json).");
                    }
                });
            });

            // 3. Register with the control plane (auto-discover LAN host if configured URL is unreachable).
            while (_pid is null)
            {
                try
                {
                    if (!await HostDiscoveryClient.IsHealthyAsync(_s.ServerUrl))
                        throw new HttpRequestException($"Organizer unreachable at {_s.ServerUrl}");
                    _pid = await _api.RegisterAsync(_s.DisplayName, _s.KioskId, _s.DeskId, _s.Password, _s.GitHubHandle, _s.GoogleAccount);
                    if (!string.IsNullOrEmpty(_api.RegisteredDeskId) && _s.DeskId != _api.RegisteredDeskId)
                    {
                        _s.DeskId = _api.RegisteredDeskId;
                        _s.Save();
                    }
                    if (!string.IsNullOrEmpty(_api.Token))
                    {
                        _s.DeskToken = _api.Token;
                        _s.Save();
                    }
                }
                catch (Exception ex)
                {
                    Status($"Waiting for server at {_s.ServerUrl} ({ex.Message}) — scanning LAN... [Click or Ctrl+Shift+S]");
                    var discovered = await HostDiscoveryClient.DiscoverAsync();
                    if (!string.IsNullOrEmpty(discovered) && !string.Equals(discovered.TrimEnd('/'), _s.ServerUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
                    {
                        _s.ServerUrl = discovered;
                        _s.Save();
                        _api = new ApiClient(_s.ServerUrl);
                        Status($"Discovered Organizer at {_s.ServerUrl}. Connecting...");
                        continue;
                    }
                    await Task.Delay(500);
                }
            }

            // 4. Sync starts held; pull the server state first so a restart can never push old files over an attack.
            _sync = new SnapshotSync(_api, _pid, _s.WorkspacePath);
            _sync.Hold();
            _pull = new WorkspaceSync(_api, _pid, _s.WorkspacePath, _sync);
            var status = await _api.GetStatusAsync(_pid);
            if (status?.ActiveAttacks != null && status.ActiveAttacks.Count > 0)
            {
                var pulled = await _pull.PullAsync("startup");
                if (pulled > 0)
                {
                    _site.RequestReload();
                    Code.RefreshAll();
                }
                foreach (var id in status.ActiveAttacks)
                {
                    _pulled.Add(id);
                    _activeAttacks.Add(id);
                }
            }
            else
            {
                _activeAttacks.Clear();
                _pulled.Clear();
                if (!WorkspaceBackup.IsWorkspaceHealthyAndNominal(_s.WorkspacePath, _s.SiteTemplatePath))
                {
                    WorkspaceBackup.ForceResetToPristine(_s.WorkspacePath, _s.SiteTemplatePath, _s.DataPath);
                    _site?.RequestReload();
                    Code.RefreshAll();
                }
            }
            _sync.Release();
            _sync.Start(TimeSpan.FromSeconds(15));
            if (status?.Status is "eliminated" or "disqualified") { Eliminate(); return; }

            // 5. Live connection to the event server.
            _hub = new HubClient(_s.ServerUrl, _pid, _api.Token ?? _s.DeskToken ?? "");
            _hub.Imminent += OnImminentAsync;
            _hub.Landed += HandleAttackAsync;
            _hub.Reset += OnAttackResetAsync;
            _hub.Message += Status;
            _hub.Paused += () => Status("Event paused by the organizers.");
            _hub.Resumed += () => Status("Event resumed.");
            _hub.Eliminated += () => Dispatcher.InvokeAsync(Eliminate);
            _hub.Disqualified += reason => Dispatcher.InvokeAsync(Eliminate);
            _hub.PardonGranted += () => Dispatcher.InvokeAsync(HandlePardonGranted);
            _hub.ProAccountAllowed += () => Dispatcher.InvokeAsync(HandleProAccountAllowed);
            _hub.ResetBaseline += () => Dispatcher.InvokeAsync(OnResetBaselineAsync);
            _hub.VerdictDetailed += OnVerdictDetailed;
            _hub.VerdictTiersDetailed += OnVerdictTiersDetailed;
            _hub.Reconnected += ResyncAsync;
            await _hub.StartAsync();

            BtnProfile.Content = $"👤 {_s.DisplayName}";

            if (_activeAttacks.Count > 0)
                Status($"Active breach detected: #{string.Join(", #", _activeAttacks)}. Fix the code, then submit.");
            else
                Status("Connected. All systems secure. No active attacks.");

            UpdateAttackBadges();
            RefreshMeters();

            // 7. Background status polling timer (every 5 seconds) to keep attacks live
            _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _statusTimer.Tick += async (_, _) => await ResyncAsync();
            _statusTimer.Start();
        }
        catch (Exception ex)
        {
            Status($"Startup problem: {ex.Message}");
        }
    }

    private void EnsureAiContextFiles()
    {
        try
        {
            var srcGemini = Path.Combine(_s.SiteTemplatePath, "GEMINI.md");
            var dstGemini = Path.Combine(_s.WorkspacePath, "GEMINI.md");
            if (File.Exists(srcGemini) && (!File.Exists(dstGemini) || new FileInfo(dstGemini).Length == 0))
                File.Copy(srcGemini, dstGemini, true);

            var srcAgents = Path.Combine(_s.SiteTemplatePath, "AGENTS.md");
            var dstAgents = Path.Combine(_s.WorkspacePath, "AGENTS.md");
            if (File.Exists(srcAgents) && (!File.Exists(dstAgents) || new FileInfo(dstAgents).Length == 0))
                File.Copy(srcAgents, dstAgents, true);
        }
        catch { /* best effort */ }
    }

    private int _terminalsStarted;

    private async Task StartTerminalsAsync()
    {
        if (Interlocked.Exchange(ref _terminalsStarted, 1) == 1) return;
        var assets = Path.Combine(AppContext.BaseDirectory, "Assets", "terminal");
        await Task.WhenAll(TermAgy.InitializeAsync(assets), TermOc.InitializeAsync(assets));

        // Security: Ensure git prohibition shim is installed in BreachKiosk/bin and prepended to PATH
        var shimDir = EnsureGitProhibitionShim();
        var sysPath = Environment.GetEnvironmentVariable("PATH") ?? "";
        var restrictedPath = string.IsNullOrEmpty(shimDir) ? sysPath : $"{shimDir};{sysPath}";

        // agy: Pre-configure trust and permissions in settings.json so no trust prompt appears
        ConfigureAgy(_s.WorkspacePath);
        var agy = ExeResolver.Resolve(_s.AgyPath);
        if (agy is null)
        {
            TermAgy.Launch(new LaunchSpec("", "", _s.WorkspacePath, new Dictionary<string, string?>(),
                () => throw new FileNotFoundException("agy was not found. Check AgyPath in kiosk.json.")));
        }
        else
        {
            var (exe, args) = ExeResolver.ForLaunch(agy, "--dangerously-skip-permissions --mode auto");
            TermAgy.Launch(new LaunchSpec(exe, args, _s.WorkspacePath, new Dictionary<string, string?>
            {
                ["PATH"] = restrictedPath,
                ["GEMINI_API_KEY"] = null,
                ["GOOGLE_API_KEY"] = null,
                ["GOOGLE_GENAI_USE_VERTEXAI"] = null,
                ["COLORTERM"] = "truecolor",
            }));
        }
        TermAgy.OutputText += _quota.Feed;
        _quota.Exhausted += () => Dispatcher.InvokeAsync(OnAgyExhausted);
        _quota.ProAccountDetected += tier => Dispatcher.InvokeAsync(() => HandleProAccountDetected(tier));
        _quota.OtherSignal += kind => _ = _api.SendTelemetryAsync(_pid!, "QUOTA_SIGNAL", new { kind });

        // OpenCode: `opencode serve` runs hidden; visible terminal attaches to it, prompts counted from server
        _oc = new OpenCodeHost(_s.OpenCodePath, _s.WorkspacePath, _s.OpenCodePort, _s.OpenCodeModel, _s.DataPath);
        _oc.PromptCounted += total => Dispatcher.InvokeAsync(() => OnPrompt(total));
        _oc.Problem += Status;
        var ocPath = ExeResolver.Resolve(_s.OpenCodePath);
        if (ocPath is null)
        {
            Status("Installing OpenCode AI CLI...");
            ocPath = await OpenCodeHost.EnsureInstalledAsync(_s.DataPath, _s.OpenCodePath);
        }
        if (ocPath is null)
        {
            TermOc.Launch(new LaunchSpec("", "", _s.WorkspacePath, new Dictionary<string, string?>(),
                () => throw new FileNotFoundException("opencode was not found. Check OpenCodePath in kiosk.json.")));
        }
        else
        {
            var (exe, args) = ExeResolver.ForLaunch(ocPath, $"attach {_oc.Url}");
            TermOc.Launch(new LaunchSpec(exe, args, _s.WorkspacePath, _oc.Environment, _oc.StartAsync));
        }
        _ = Dispatcher.InvokeAsync(TermAgy.FocusTerminal);
    }

    /// <summary>
    /// Creates a security interception wrapper in %LOCALAPPDATA%\BreachKiosk\bin to completely
    /// prevent participants and AI assistants from running git diff, git status, git log, or git revert.
    /// </summary>
    private static string EnsureGitProhibitionShim()
    {
        try
        {
            var shimDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BreachKiosk", "bin");
            Directory.CreateDirectory(shimDir);
            const string script = "@echo off\r\n" +
                                  "echo [SECURITY VIOLATION] Git inspection, git diff, and git revert are strictly prohibited during Breach Protocol.\r\n" +
                                  "echo Diagnose vulnerabilities directly from application logs, behavior, and source files.\r\n" +
                                  "exit /b 1\r\n";
            File.WriteAllText(Path.Combine(shimDir, "git.cmd"), script);
            File.WriteAllText(Path.Combine(shimDir, "git.bat"), script);

            const string netShim = "@echo off\r\n" +
                                   "set \"ARGS=%*\"\r\n" +
                                   "echo %ARGS% | findstr /i \"8000 5080 openapi attacks evaluate\" >nul\r\n" +
                                   "if %errorlevel% equ 0 (\r\n" +
                                   "    echo [SECURITY VIOLATION] Host inspection, internal port probing, and evaluate API calls are strictly prohibited during Breach Protocol.\r\n" +
                                   "    echo Submit defenses and review diagnostics exclusively via the LabKiosk interface.\r\n" +
                                   "    exit /b 1\r\n" +
                                   ")\r\n" +
                                   "if exist \"%SystemRoot%\\System32\\curl.exe\" (\r\n" +
                                   "    \"%SystemRoot%\\System32\\curl.exe\" %*\r\n" +
                                   ") else (\r\n" +
                                   "    exit /b 1\r\n" +
                                   ")\r\n";
            File.WriteAllText(Path.Combine(shimDir, "curl.cmd"), netShim);
            File.WriteAllText(Path.Combine(shimDir, "curl.bat"), netShim);
            File.WriteAllText(Path.Combine(shimDir, "wget.cmd"), netShim);
            File.WriteAllText(Path.Combine(shimDir, "wget.bat"), netShim);
            return shimDir;
        }
        catch { return ""; }
    }

    // ---- attacks -------------------------------------------------------------------------------------
    private async Task OnImminentAsync(int attackId, int seconds)
    {
        var name = AttackNames.TryGetValue(attackId, out var n) ? n : $"Attack #{attackId}";
        Status($"🚨 BREACH IMMINENT: #{attackId} ({name}) in {seconds}s! Saving workspace...");
        Sfx.Siren();
        if (_sync is null || _hub is null) return;
        WorkspaceBackup.Snapshot(_s.WorkspacePath, $"pre-attack-{attackId}");
        await _sync.FlushAsync(force: true);
        _sync.Hold();
        await _hub.AckImminentAsync(attackId);
    }

    private static string RouteForAttack(int attackId) => attackId switch
    {
        1 or 3 or 7 or 9 => "/traffic",
        2 or 4 or 8 => "/vault",
        5 => "/power",
        _ => "/",
    };

    private void NavigateSiteToRoute(string route)
    {
        var target = _s.SiteUrl.TrimEnd('/') + route;
        if (Site.CoreWebView2 is not null)
        {
            var current = Site.CoreWebView2.Source?.TrimEnd('/');
            if (string.Equals(current, target.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
                Site.CoreWebView2.Reload();
            else
                Site.CoreWebView2.Navigate(target);
        }
        else
        {
            if (Uri.TryCreate(target, UriKind.Absolute, out var targetUri))
                Site.Source = targetUri;
            else
                Site.Source = new Uri("http://127.0.0.1:3000");
        }
    }

    private void ShowSiteDownInWebView(string? error = null)
    {
        _isSiteDownViewShowing = true;
        if (Site.CoreWebView2 is null) return;
        var errHtml = string.IsNullOrWhiteSpace(error)
            ? "Connection refused: The municipal application process exited unexpectedly or failed health checks."
            : System.Net.WebUtility.HtmlEncode(error.Trim());

        var html = $$"""
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <title>500 // SERVICE UNHEALTHY</title>
              <style>
                body {
                  background: #070d19;
                  color: #e2e8f0;
                  font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, "Consolas", monospace;
                  margin: 0;
                  padding: 2.5rem;
                  display: flex;
                  flex-direction: column;
                  align-items: center;
                  justify-content: center;
                  min-height: 80vh;
                  box-sizing: border-box;
                }
                .card {
                  background: rgba(18, 26, 43, 0.95);
                  border: 1px solid #ef4444;
                  border-top: 4px solid #ef4444;
                  border-radius: 8px;
                  max-width: 820px;
                  width: 100%;
                  padding: 2rem;
                  box-shadow: 0 10px 25px -5px rgba(0, 0, 0, 0.6);
                }
                .badge {
                  display: inline-block;
                  background: rgba(239, 68, 68, 0.2);
                  color: #f87171;
                  padding: 3px 8px;
                  border-radius: 4px;
                  font-size: 11px;
                  font-weight: 700;
                  letter-spacing: 0.05em;
                  margin-bottom: 1rem;
                }
                h1 {
                  font-size: 1.4rem;
                  margin: 0 0 0.5rem 0;
                  color: #ef4444;
                  letter-spacing: 0.02em;
                }
                p {
                  color: #94a3b8;
                  font-size: 0.95rem;
                  line-height: 1.5;
                  margin: 0 0 1.5rem 0;
                }
                pre {
                  background: #020617;
                  border: 1px solid #1e293b;
                  border-radius: 6px;
                  padding: 1rem;
                  color: #fca5a5;
                  font-family: "Consolas", "Courier New", monospace;
                  font-size: 0.85rem;
                  overflow-x: auto;
                  white-space: pre-wrap;
                  word-break: break-all;
                  max-height: 320px;
                  line-height: 1.4;
                  margin: 0;
                }
                .footer {
                  margin-top: 1.5rem;
                  font-size: 0.85rem;
                  color: #64748b;
                  display: flex;
                  justify-content: space-between;
                }
              </style>
            </head>
            <body>
              <div class="card">
                <span class="badge">DEFCON-1 // NODE RUNTIME CRASH</span>
                <h1>500 // SERVICE UNHEALTHY (NODE CRASH)</h1>
                <p>The local municipal web application crashed or failed to boot. Inspect the runtime error trace below, resolve any syntax or runtime exceptions in your code, and save to restart.</p>
                <pre><code>{{errHtml}}</code></pre>
                <div class="footer">
                  <span>AEGIS Municipal Defensive Grid</span>
                  <span>Auto-heals on save when valid</span>
                </div>
              </div>
            </body>
            </html>
            """;
        Site.CoreWebView2.NavigateToString(html);
    }

    private async Task HandleAttackAsync(int attackId)
    {
        if (_sync is null || _pull is null) return;
        _sync.Hold();
        try
        {
            int n;
            List<string> patchedPaths;
            try
            {
                var payload = await _api.GetAttackPayloadAsync(_pid!, attackId);
                patchedPaths = payload.Select(f => f.Path).ToList();
                n = await _pull.ApplyAsync($"attack-{attackId}", payload);
            }
            catch
            {
                n = await _pull.PullAsync($"attack-{attackId}");
                patchedPaths = new List<string>();
            }
            // Record baseline AFTER attack files land — anything outside patchedPaths is off-limits
            _pull.RecordBaseline(patchedPaths);
            _pulled.Add(attackId);
            _activeAttacks.Add(attackId);
            var name = AttackNames.TryGetValue(attackId, out var nm) ? nm : $"Attack #{attackId}";
            WorkspaceBackup.WipeLocalGitRepo(_s.WorkspacePath);
            Status($"🚨 ATTACK #{attackId} ({name}) LANDED! ({n} files changed). Fix the code, then submit.");
            var healthy = _site is not null && await _site.WaitRestartHealthyAsync();
            Code.RefreshAll();
            await Dispatcher.InvokeAsync(() =>
            {
                if (_site is null || healthy)
                    NavigateSiteToRoute(RouteForAttack(attackId));
                else
                    ShowSiteDownInWebView();
                UpdateAttackBadges();
                ShowIncident(attackId);
            });
        }
        catch (Exception ex)
        {
            Status($"Could not download attack #{attackId}: {ex.Message}. Retrying...");
        }
        finally { _sync.Release(); }
    }

    private async Task ResyncAsync()
    {
        if (_pid is null) return;
        try
        {
            var status = await _api.GetStatusAsync(_pid);
            if (status is null) return;
            if (status.Status is "eliminated" or "disqualified") { await Dispatcher.InvokeAsync(Eliminate); return; }

            var newlyLanded = status.ActiveAttacks.Where(a => !_pulled.Contains(a)).ToList();
            foreach (var id in newlyLanded)
                await HandleAttackAsync(id);

            Dispatcher.Invoke(() =>
            {
                var changed = false;
                foreach (var a in status.ActiveAttacks)
                {
                    if (_activeAttacks.Add(a)) changed = true;
                }
                var toRemove = _activeAttacks.Where(a => !status.ActiveAttacks.Contains(a) && !_solvedAttacks.Contains(a)).ToList();
                foreach (var rem in toRemove)
                {
                    _activeAttacks.Remove(rem);
                    _pulled.Remove(rem);
                    changed = true;
                    if (_activeIncidentWindow?.AttackId == rem)
                    {
                        try { _activeIncidentWindow.Close(); } catch { }
                        _activeIncidentWindow = null;
                    }
                }

                if (changed) UpdateAttackBadges();
            });
        }
        catch { /* retry next interval */ }
    }

    private void UpdateAttackBadges()
    {
        AttackBadgesPanel.Children.Clear();

        if (_activeAttacks.Count == 0 && _solvedAttacks.Count == 0)
        {
            var tb = new TextBlock
            {
                Text = "🛡️ All systems secure. Standing by for incoming breaches...",
                Foreground = (Brush)FindResource("Mute"),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            };
            AttackBadgesPanel.Children.Add(tb);
            return;
        }

        foreach (var id in _activeAttacks.OrderBy(x => x))
        {
            var name = AttackNames.TryGetValue(id, out var n) ? n : $"Attack #{id}";
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(60, 229, 83, 61)),
                BorderBrush = (Brush)FindResource("Alert"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(0, 0, 8, 0),
                Cursor = Cursors.Hand,
                ToolTip = "Click to inspect incident diagnostics and open breached file"
            };
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(new TextBlock
            {
                Text = $"⚡ #{id} {name} [ACTIVE]",
                Foreground = (Brush)FindResource("Alert"),
                FontWeight = FontWeights.Bold,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            });
            border.Child = content;
            var localId = id;
            border.MouseLeftButtonUp += (_, _) => ShowIncident(localId);
            AttackBadgesPanel.Children.Add(border);
        }

        foreach (var id in _solvedAttacks.OrderBy(x => x))
        {
            var name = AttackNames.TryGetValue(id, out var n) ? n : $"Attack #{id}";
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(50, 63, 182, 139)),
                BorderBrush = (Brush)FindResource("Ok"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(0, 0, 8, 0),
                Cursor = Cursors.Hand,
                ToolTip = "Resolved breach (click to review incident specifications)"
            };
            var tb = new TextBlock
            {
                Text = $"✔ #{id} {name} [SOLVED]",
                Foreground = (Brush)FindResource("Ok"),
                FontWeight = FontWeights.SemiBold,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            };
            border.Child = tb;
            var localId = id;
            border.MouseLeftButtonUp += (_, _) => ShowIncident(localId);
            AttackBadgesPanel.Children.Add(border);
        }
    }

    private void OnVerdictTiersDetailed(int id, string st, string reason, double pts, Dictionary<string, bool>? tiers)
    {
        if (tiers != null) _lastTierResults[id] = tiers;
        OnVerdictDetailed(id, st, reason, pts);
    }

    private void OnVerdictDetailed(int id, string st, string reason, double pts)
    {
        _submitTimeoutCts?.Cancel();
        Dispatcher.InvokeAsync(() =>
        {
            SubmitButton.IsEnabled = !_eliminated;
            var attackName = AttackNames.TryGetValue(id, out var nm) ? nm : $"Attack #{id}";
            var isSolved = st is "SOLVED" or "OPTIMAL" or "BASE";
            var isProgressive = st is "PROGRESSIVE";
            if (isSolved)
            {
                _activeAttacks.Remove(id);
                _solvedAttacks.Add(id);
                WorkspaceBackup.WipeLocalGitRepo(_s.WorkspacePath);
                _pull?.ClearBaseline();
                Status($"🎉 #{id} {st} (+{pts:0.#} pts) | {reason}");
                CelebrateSolve();
            }
            else if (isProgressive)
            {
                Status($"⚡ #{id} PROGRESSIVE (+{pts:0.#} pts) | {reason}");
            }
            else
            {
                Status($"❌ #{id} {st}: {reason}");
            }
            UpdateAttackBadges();
            RefreshMeters();
            _site?.RequestReload();

            var tiers = _lastTierResults.TryGetValue(id, out var tr) ? tr : null;
            UpdateDefenseLedHud(tiers, st);
            try
            {
                var modal = new VerdictWindow(this, id, attackName, st, pts, reason, tiers);
                modal.ShowDialog();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error showing verdict dialog: {ex}");
            }
        });
    }

    private void UpdateDefenseLedHud(Dictionary<string, bool>? tiers, string status)
    {
        if (tiers == null || tiers.Count == 0)
        {
            if (status is "OPTIMAL" or "SOLVED")
            {
                DefenseLedBadge.Visibility = Visibility.Visible;
                DefenseLedStrip.Children.Clear();
                var greenBrush = (Brush)FindResource("Ok");
                for (int i = 0; i < 10; i++)
                {
                    DefenseLedStrip.Children.Add(new Border
                    {
                        Width = 8,
                        Height = 12,
                        Background = greenBrush,
                        CornerRadius = new CornerRadius(1.5),
                        Margin = new Thickness(1, 0, 1, 0)
                    });
                }
                DefenseLedPct.Text = "100%";
                DefenseLedPct.Foreground = greenBrush;
            }
            return;
        }

        DefenseLedBadge.Visibility = Visibility.Visible;
        DefenseLedStrip.Children.Clear();
        var okBrush = (Brush)FindResource("Ok");
        var redBrush = (Brush)FindResource("Alert");

        int passed = 0;
        foreach (var val in tiers.Values)
        {
            if (val) passed++;
            DefenseLedStrip.Children.Add(new Border
            {
                Width = 8,
                Height = 12,
                Background = val ? okBrush : redBrush,
                CornerRadius = new CornerRadius(1.5),
                Margin = new Thickness(1, 0, 1, 0)
            });
        }

        int total = tiers.Count;
        double pct = total > 0 ? (double)passed / total * 100.0 : 0.0;
        DefenseLedPct.Text = $"{pct:0}%";
        DefenseLedPct.Foreground = passed == total ? okBrush : (passed > 0 ? (Brush)FindResource("Cyan") : redBrush);
    }

    private void OnVerdict(string submissionId, JsonElement verdicts, string note)
    {
        _submitTimeoutCts?.Cancel();
        var parts = new List<string>();
        var anySolved = false;
        foreach (var v in verdicts.EnumerateArray())
        {
            var id = v.GetProperty("attackId").GetInt32();
            var st = v.GetProperty("status").GetString() ?? "FAILED";
            var reason = v.TryGetProperty("reason", out var rProp) ? (rProp.GetString() ?? "") : "";
            var pts = v.TryGetProperty("points", out var pProp) ? pProp.GetDouble() : 0.0;
            var isSolved = st is "SOLVED" or "OPTIMAL" or "BASE";
            var isProgressive = st is "PROGRESSIVE";

            if (isSolved)
            {
                anySolved = true;
                _activeAttacks.Remove(id);
                _solvedAttacks.Add(id);
                WorkspaceBackup.WipeLocalGitRepo(_s.WorkspacePath);
                parts.Add($"#{id} {st} +{pts:0.#} pts");
                _pull?.ClearBaseline(); // penalty window closed — attack is done
            }
            else if (isProgressive)
            {
                parts.Add($"#{id} PROGRESSIVE +{pts:0.#} pts");
            }
            else
            {
                parts.Add($"#{id} {st}: {reason}");
            }

            var attackName = AttackNames.TryGetValue(id, out var nm) ? nm : $"Attack #{id}";
            Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    var tiers = _lastTierResults.TryGetValue(id, out var tr) ? tr : null;
                    UpdateDefenseLedHud(tiers, st);
                    var modal = new VerdictWindow(this, id, attackName, st, pts, reason, tiers);
                    modal.ShowDialog();
                }
                catch { }
            });
        }
        Status(parts.Count > 0 ? string.Join("   |   ", parts) : note);
        Dispatcher.InvokeAsync(() =>
        {
            SubmitButton.IsEnabled = !_eliminated;
            UpdateAttackBadges();
            if (anySolved)
            {
                CelebrateSolve();
                _isSiteDownViewShowing = false;
                NavigateSiteToRoute("/");
            }
            _site?.RequestReload();
        });
    }

    private void CelebrateSolve()
    {
        Sfx.Fanfare();
        SolvedBadge.Visibility = Visibility.Visible;
        var glow = new DoubleAnimation(4, 26, TimeSpan.FromMilliseconds(450)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(6) };
        ((DropShadowEffect)SolvedBadge.Effect).BeginAnimation(DropShadowEffect.BlurRadiusProperty, glow);
        _ = Task.Delay(TimeSpan.FromSeconds(8)).ContinueWith(_ => Dispatcher.InvokeAsync(() => SolvedBadge.Visibility = Visibility.Collapsed));
    }

    // ---- budgets -------------------------------------------------------------------------------------
    private void OnAgyExhausted()
    {
        if (_proPlanAuthorized || _quota.InhibitExhaustion) return;
        if (_budget.AgyExhausted) return;
        _budget.MarkAgyExhausted();
        TabOc.IsChecked = true;
        Status("⚠️ Antigravity AI reported quota limits. Switched to OpenCode AI tab (or switch models in agy).");
        _ = _api.SendTelemetryAsync(_pid!, "AGY_EXHAUSTED");
        RefreshMeters();
        CheckBothSpent();
    }

    private void OnPrompt(int total)
    {
        _budget.SetOpenCodePrompts(total);
        for (var n = _reportedPrompts + 1; n <= total; n++)
            _ = _api.SendTelemetryAsync(_pid!, "PROMPT", new { agent = "opencode", n });
        _reportedPrompts = total;
        RefreshMeters();

        if (_budget.OpenCodeCapReached && !_capLockScheduled)
        {
            _capLockScheduled = true;
            _ = _api.SendTelemetryAsync(_pid!, "OC_CAP_REACHED");
            _ = Task.Delay(TimeSpan.FromSeconds(60)).ContinueWith(_ => Dispatcher.InvokeAsync(() =>
            {
                _oc?.Stop();
                TermOc.Lock("OpenCode prompt limit reached.");
                CheckBothSpent();
            }));
        }
    }

    private void CheckBothSpent()
    {
        if (_budget.BothSpent) Status("Both AI budgets are used up. An organizer will confirm your result.");
    }

    private void Eliminate()
    {
        _eliminated = true;
        TermAgy.Lock("Your session has ended.");
        TermOc.Lock("Your session has ended.");
        _oc?.Stop();
        _sync?.Hold();
        SubmitButton.IsEnabled = false;
        Status("Your session has ended. Thanks for playing.");
    }

    // ---- UI & View Switching -------------------------------------------------------------------------
    private async void OnSubmitFix(object sender, RoutedEventArgs e)
    {
        if (_pid is null || _sync is null) return;
        SubmitButton.IsEnabled = false;
        try
        {
            // ---- Scope validation gate ----------------------------------------------------------------
            if (_pull is not null)
            {
                var violations = _pull.CheckOutOfScopeEdits();
                if (violations.Count > 0)
                {
                    var fileList = string.Join("\n  • ", violations.Take(8));
                    var msg = $"⛔ SUBMISSION BLOCKED — UNEXPECTED CODE MODIFICATION\n\n" +
                              $"Modified {violations.Count} file(s) outside the scope of this active incident:\n\n" +
                              $"  • {fileList}\n\n" +
                              $"Only source code directly responsible for the active defect should be edited.\n" +
                              $"Please revert unintended modifications and re-submit.";
                    MessageBox.Show(msg, "Scope Verification", MessageBoxButton.OK, MessageBoxImage.Warning);
                    Status($"⛔ Blocked: {violations.Count} out-of-scope file(s) modified. Revert them and re-submit.");
                    _ = _api.SendTelemetryAsync(_pid, "TOUCH_PENALTY", new
                    {
                        reason = "UNAUTHORIZED_FILE_MODIFICATION",
                        violations,
                        files = violations,
                        penalty = -300
                    });
                    SubmitButton.IsEnabled = !_eliminated;
                    return;
                }
            }
            // ---- Normal submit flow ----------------------------------------------------------------
            Status("Uploading your fix to evaluation engine...");
            await _sync.FlushAsync(force: true);
            var subId = await _api.SubmitAsync(_pid);
            Status("Fix submitted! Automated test & security checks are running... [0s / 60s]");

            // Safety timeout & live status ticker: if verdict is delayed, poll status or re-enable submit
            _submitTimeoutCts?.Cancel();
            _submitTimeoutCts = new CancellationTokenSource();
            var token = _submitTimeoutCts.Token;
            _ = Task.Run(async () =>
            {
                try
                {
                    for (int elapsed = 1; elapsed <= 65; elapsed++)
                    {
                        await Task.Delay(1000, token);
                        if (token.IsCancellationRequested) return;

                        // Fallback polling every 2 seconds if submissionId is known
                        if (!string.IsNullOrEmpty(subId) && elapsed >= 2 && elapsed % 2 == 0)
                        {
                            var statusDoc = await _api.GetSubmissionStatusAsync(_pid!, subId);
                            if (statusDoc.HasValue && statusDoc.Value.TryGetProperty("results", out var resArr) && resArr.GetArrayLength() > 0)
                            {
                                foreach (var item in resArr.EnumerateArray())
                                {
                                    var aid = item.GetProperty("attackId").GetInt32();
                                    var st = item.GetProperty("status").GetString() ?? "FAILED";
                                    var rsn = item.GetProperty("reason").GetString() ?? "";
                                    var pts = item.GetProperty("points").GetDouble();
                                    OnVerdictDetailed(aid, st, rsn, pts);
                                }
                                return;
                            }
                        }

                        if (elapsed < 60 && elapsed % 5 == 0)
                        {
                            await Dispatcher.InvokeAsync(() =>
                            {
                                Status($"Automated test & security checks running... [{elapsed}s / 60s]");
                            });
                        }
                    }
                    if (!token.IsCancellationRequested && !_eliminated)
                    {
                        await Dispatcher.InvokeAsync(() =>
                        {
                            SubmitButton.IsEnabled = true;
                            Status("Evaluation completed or queued for deep analysis. You may submit again if needed.");
                        });
                    }
                }
                catch { }
            }, token);
        }
        catch (Exception ex)
        {
            Status($"Submit failed: {ex.Message}. Try again.");
            SubmitButton.IsEnabled = !_eliminated;
        }
    }

    private void OnViewSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ViewSelector?.SelectedItem is ComboBoxItem item && item.Tag is string mode)
            ApplyViewLayout(mode);
    }

    private void SelectViewMode(string mode)
    {
        if (ViewSelector is not null)
        {
            foreach (ComboBoxItem item in ViewSelector.Items)
            {
                if (item.Tag as string == mode)
                {
                    ViewSelector.SelectedItem = item;
                    break;
                }
            }
        }
        ApplyViewLayout(mode);
    }

    private void ApplyViewLayout(string mode)
    {
        if (ColSite is null || ColCode is null || ColTerminal is null) return;
        var (site, code, ai) = mode switch
        {
            "CodeAi"  => (0.0,  1.0,  1.0),
            "WebAi"   => (1.0,  0.0,  1.0),
            "WebCode" => (1.0,  1.0,  0.0),
            "Site"    => (1.0,  0.0,  0.0),
            "Code"    => (0.0,  1.0,  0.0),
            "Ai"      => (0.0,  0.0,  1.0),
            _         => (1.15, 1.15, 1.2),
        };
        ColSite.Width = new GridLength(site, site > 0 ? GridUnitType.Star : GridUnitType.Pixel);
        ColCode.Width = new GridLength(code, code > 0 ? GridUnitType.Star : GridUnitType.Pixel);
        ColTerminal.Width = new GridLength(ai, ai > 0 ? GridUnitType.Star : GridUnitType.Pixel);

        var show1 = site > 0 && (code > 0 || ai > 0);
        var show2 = code > 0 && ai > 0;
        Splitter1Col.Width = new GridLength(show1 ? 6 : 0);
        Splitter1.Visibility = show1 ? Visibility.Visible : Visibility.Collapsed;
        Splitter2Col.Width = new GridLength(show2 ? 6 : 0);
        Splitter2.Visibility = show2 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---- incident popup & investigation -------------------------------------------------------------
    public void ShowIncident(int attackId)
    {
        if (_activeIncidentWindow != null)
        {
            try { _activeIncidentWindow.Close(); } catch { }
            _activeIncidentWindow = null;
        }

        if (AttackMetadata.TryGetValue(attackId, out var meta))
        {
            _activeIncidentWindow = new IncidentWindow(
                this,
                attackId,
                meta.Title,
                meta.Classification,
                meta.Symptoms,
                meta.Telemetry,
                onInvestigate: OnInvestigateWorkspace);
        }
        else
        {
            _activeIncidentWindow = new IncidentWindow(
                this,
                attackId,
                $"Attack #{attackId}: Security Breach Detected",
                "CRITICAL SEVERITY · INTRUSION ALERT",
                "Unauthorized modifications detected across application services.",
                "SIEM telemetry flags abnormal runtime behavior on active endpoints. Review workspace changes.",
                onInvestigate: OnInvestigateWorkspace);
        }

        _activeIncidentWindow.Show();
    }

    private void OnInvestigateWorkspace()
    {
        // Switch view to Code + AI so participant can inspect files with their AI assistant
        // Does NOT pre-open or reveal any file spoiler
        SelectViewMode("CodeAi");
    }

    private async Task OnAttackResetAsync(int attackId)
    {
        if (_sync is null || _pull is null) return;
        _sync.Hold();
        try
        {
            _activeAttacks.Remove(attackId);
            _pulled.Remove(attackId);
            _solvedAttacks.Remove(attackId);

            // Pull clean state from server
            await _pull.PullAsync($"reset-{attackId}");

            var name = AttackNames.TryGetValue(attackId, out var nm) ? nm : $"Attack #{attackId}";
            WorkspaceBackup.WipeLocalGitRepo(_s.WorkspacePath);
            Status($"↺ Attack #{attackId} ({name}) reset by organizer. Workspace restored.");

            _site?.RequestReload();
            Code.RefreshAll();

            Dispatcher.Invoke(() =>
            {
                if (_activeIncidentWindow?.AttackId == attackId)
                {
                    try { _activeIncidentWindow.Close(); } catch { }
                    _activeIncidentWindow = null;
                }
                UpdateAttackBadges();
            });
        }
        catch (Exception ex)
        {
            Status($"Failed to reset attack #{attackId}: {ex.Message}");
        }
        finally
        {
            _sync.Release();
        }
    }

    private async Task OnResetBaselineAsync()
    {
        _sync?.Hold();
        try
        {
            _activeAttacks.Clear();
            _pulled.Clear();
            _solvedAttacks.Clear();

            WorkspaceBackup.ForceResetToPristine(_s.WorkspacePath, _s.SiteTemplatePath, _s.DataPath);
            _site?.RequestReload();
            Code.RefreshAll();

            await Dispatcher.InvokeAsync(() =>
            {
                try { _activeIncidentWindow?.Close(); } catch { }
                _activeIncidentWindow = null;
                UpdateAttackBadges();
                Status("↺ Workspace restored to pristine DEFCON-4 baseline. All systems nominal.");
                NavigateSiteToRoute("/");
            });
        }
        catch (Exception ex)
        {
            Status($"Reset baseline failed: {ex.Message}");
        }
        finally
        {
            _sync?.Release();
        }
    }

    private void OnProfileClick(object sender, RoutedEventArgs e)
    {
        var onboard = new OnboardingWindow(_s, _hub) { Owner = this };
        if (onboard.ShowDialog() == true)
        {
            BtnProfile.Content = $"👤 {_s.DisplayName}";
            Status($"Identity updated to {_s.DisplayName}");
        }
    }

    private void HandleProAccountDetected(string tier)
    {
        if (_proPlanAuthorized || _quota.InhibitExhaustion) return;
        TermAgy.Lock("COMPLIANCE REVIEW: Unauthorized Google AI Pro tier detected.");
        Status("🚨 COMPLIANCE ALERT: Google AI Pro detected. Transmitted to event arbiters.");
        var dlg = new ComplianceLockoutDialog(_hub, _s.GoogleAccount, tier, _s.DeskId ?? "")
        {
            Owner = this
        };
        var res = dlg.ShowDialog();
        if (res == true)
        {
            if (dlg.AllowedProPlan)
            {
                HandleProAccountAllowed();
            }
            else
            {
                HandlePardonGranted();
            }
        }
        else
        {
            Eliminate();
        }
    }

    private void HandleProAccountAllowed()
    {
        _proPlanAuthorized = true;
        _quota.InhibitExhaustion = true;
        _quota.Reset();
        _budget.ResetAgy();
        TermAgy.UnlockAndRestart();
        TabAgy.IsChecked = true;
        Status("⚡ Google AI Pro plan authorized by Organizer. Antigravity AI unlocked.");
        RefreshMeters();
    }

    private void HandlePardonGranted()
    {
        CredentialManager.ClearAntigravityGoogleCredential();
        _s.GoogleAccount = "";
        _s.Save();
        _quota.Reset();
        Status("🕊️ Arbiter pardon granted. Please sign in with an approved standard Google account.");

        var onboard = new OnboardingWindow(_s, _hub) { Owner = this };
        if (onboard.ShowDialog() == true)
        {
            _terminalsStarted = 0;
            _ = StartTerminalsAsync();
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5) { e.Handled = true; if (SubmitButton.IsEnabled) OnSubmitFix(sender, e); }
        else if (e.Key == Key.A && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift)) { e.Handled = true; OnAdminConsole(sender, e); }
        else if (e.Key == Key.S && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift)) { e.Handled = true; PromptChangeServerUrl(); }
    }

    private void OnStatusClick(object sender, MouseButtonEventArgs e)
    {
        if (_pid is null)
            PromptChangeServerUrl();
    }

    private void PromptChangeServerUrl()
    {
        var newUrl = ServerPrompt.Ask(this, _s.ServerUrl);
        if (newUrl is not null && newUrl != _s.ServerUrl)
        {
            _s.ServerUrl = newUrl;
            ServerPrompt.SaveServerUrl(newUrl);
            _api = new ApiClient(_s.ServerUrl);
            Status($"Server URL set to {_s.ServerUrl}. Reconnecting...");
        }
    }

    private async void OnAdminConsole(object sender, RoutedEventArgs e)
    {
        var key = KeyPrompt.Ask(this);
        if (key is null) return;
        try
        {
            using var http = new HttpClient { BaseAddress = new Uri(KioskSettings.NormalizeServerUrl(_s.ServerUrl)), Timeout = TimeSpan.FromSeconds(5) };
            http.DefaultRequestHeaders.Add("X-Admin-Key", key);
            using var res = await http.GetAsync("/admin/attacks");
            if (!res.IsSuccessStatusCode) { Status("That organizer key was not accepted."); return; }
            new AdminWindow(_s.ServerUrl, key).Show();
        }
        catch (Exception ex) { Status($"Could not reach the server: {ex.Message}"); }
    }

    private void OnTabChanged(object sender, RoutedEventArgs e)
    {
        if (TermAgy is null || TermOc is null) return;
        var agy = TabAgy.IsChecked == true;
        TermAgy.Visibility = agy ? Visibility.Visible : Visibility.Collapsed;
        TermOc.Visibility = agy ? Visibility.Collapsed : Visibility.Visible;
        Dispatcher.InvokeAsync(async () =>
        {
            await Task.Delay(50);
            if (agy) TermAgy.FocusTerminal();
            else TermOc.FocusTerminal();
        });
    }

    private void RefreshMeters()
    {
        AgyMeter.Text = _budget.AgyExhausted ? "agy: used up" : "agy: ready";
        OcMeter.Text = $"OpenCode: {_budget.OpenCodePrompts} / {_budget.OpenCodeCap}";
    }

    /// <summary>
    /// Ensures agy's settings file exists, is writable (never ReadOnly), and contains workspace in trustedWorkspaces.
    /// </summary>
    internal static void ConfigureAgy(string workspacePath)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity-cli");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "settings.json");

            if (File.Exists(path))
            {
                var attrs = File.GetAttributes(path);
                if ((attrs & FileAttributes.ReadOnly) != 0)
                    File.SetAttributes(path, attrs & ~FileAttributes.ReadOnly);
            }

            var doc = new Dictionary<string, object>();
            if (File.Exists(path))
            {
                try
                {
                    var text = File.ReadAllText(path);
                    doc = JsonSerializer.Deserialize<Dictionary<string, object>>(text) ?? new();
                }
                catch { }
            }

            doc["colorScheme"] = "dark";

            var trustedList = new List<string>();
            if (doc.TryGetValue("trustedWorkspaces", out var existing) && existing is JsonElement el && el.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in el.EnumerateArray())
                    if (item.GetString() is { } s && !string.IsNullOrWhiteSpace(s))
                        trustedList.Add(s);
            }

            void AddPath(string p)
            {
                if (!trustedList.Contains(p, StringComparer.OrdinalIgnoreCase))
                    trustedList.Add(p);
            }

            AddPath(workspacePath);
            AddPath(workspacePath.ToLowerInvariant());
            try { AddPath(Path.GetFullPath(workspacePath)); } catch { }
            doc["trustedWorkspaces"] = trustedList;

            var json = JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch { /* best effort */ }
    }
}
