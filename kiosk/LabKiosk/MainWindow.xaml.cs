using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
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

    private record AttackMeta(string Title, string Classification, string Symptoms, string Telemetry);

    private static readonly Dictionary<int, AttackMeta> AttackMetadata = new()
    {
        [0] = new(
            "Incident #0: Warmup Calibration Alert",
            "LOW SEVERITY · DIAGNOSTIC CHECK",
            "Startup diagnostics detected an unexpected portal configuration discrepancy.",
            "Inspect the live application and workspace to restore nominal status."
        ),
        [1] = new(
            "Incident #1: Portal Integrity Alert",
            "HIGH SEVERITY · SERVICE ANOMALY",
            "Abnormal portal behavior and unexpected response output observed on active views.",
            "SIEM alerts indicate unauthorized runtime modifications affecting live client responses."
        ),
        [2] = new(
            "Incident #2: Feed Rendering Alert",
            "CRITICAL SEVERITY · RUNTIME ANOMALY",
            "Portal content displays and user-submitted entries are producing unexpected client behavior.",
            "Runtime monitors flagged integrity violations during standard page loads."
        ),
        [3] = new(
            "Incident #3: Data Layer Anomaly",
            "CRITICAL SEVERITY · DATA INTEGRITY",
            "Unexpected record exposure and irregular responses observed during portal lookups.",
            "Backend telemetry flagged abnormal data store activity under client requests."
        ),
        [4] = new(
            "Incident #4: Dispatch Control Alert",
            "CRITICAL SEVERITY · ACCESS CONTROL",
            "Restricted operational actions are completing outside expected authorization boundaries.",
            "Audit logs show privileged state changes from unverified requests."
        ),
        [5] = new(
            "Incident #5: Vault Boundary Alert",
            "HIGH SEVERITY · ACCESS EXPOSURE",
            "Protected records are accessible outside intended caller restrictions.",
            "Access telemetry detected unauthorized data retrieval across service endpoints."
        ),
        [6] = new(
            "Incident #6: Service Responsiveness Alert",
            "HIGH SEVERITY · AVAILABILITY ANOMALY",
            "Application response latency spiked sharply across active endpoints.",
            "Process monitors indicate severe resource contention during request handling."
        ),
        [7] = new(
            "Incident #7: Runtime Bootstrap Alert",
            "CRITICAL SEVERITY · EXECUTION INTEGRITY",
            "Anomalous background activity detected during application startup and execution.",
            "Host runtime monitors flagged unexpected behavior outside normal request handling."
        ),
        [8] = new(
            "Incident #8: Identity Verification Alert",
            "HIGH SEVERITY · SESSION INTEGRITY",
            "Incoming requests are acquiring elevated privilege state inconsistently.",
            "Session telemetry detected mismatched identity state across requests."
        ),
        [9] = new(
            "Incident #9: State Consistency Alert",
            "HIGH SEVERITY · DATA INTEGRITY",
            "Routine user updates are causing unexpected internal account state changes.",
            "State audit logs show unauthorized attribute modifications during standard submissions."
        ),
        [10] = new(
            "Incident #10: Asset Boundary Alert",
            "CRITICAL SEVERITY · RESOURCE EXPOSURE",
            "Static resource handlers are serving unintended non-public workspace content.",
            "Edge monitors flagged irregular resource requests bypassing expected boundaries."
        ),
        [11] = new(
            "Incident #11: Core System Fracture",
            "CRITICAL SEVERITY · MULTI-SYSTEM ANOMALY",
            "Multiple portal subsystems are failing consistently and leaking restricted operational state.",
            "Correlated alerts across identity and service layers indicate compounding workspace faults."
        ),
        [12] = new(
            "Incident #12: Ouroboros (The Shadow Variable)",
            "CRITICAL SEVERITY · RBAC DESYNC",
            "Restricted admin and operator endpoints are accepting lower-privilege sessions despite route guards.",
            "Audit telemetry indicates centralized role checks are bypassing expected privilege boundaries."
        )
    };

    private static readonly Dictionary<int, string> AttackNames = new()
    {
        [0] = "Warmup Calibration",
        [1] = "Portal Integrity Alert",
        [2] = "Feed Rendering Alert",
        [3] = "Data Layer Anomaly",
        [4] = "Dispatch Control Alert",
        [5] = "Vault Boundary Alert",
        [6] = "Worker Latency Spike",
        [7] = "Runtime Bootstrap Alert",
        [8] = "Identity Verification Alert",
        [9] = "State Consistency Alert",
        [10] = "Asset Boundary Alert",
        [11] = "Core System Fracture",
        [12] = "Ouroboros: The Shadow Variable"
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
    private static readonly bool AppLockoutEnabled = false;
    private readonly KeyboardHook? _keyboardHook;
    private bool _isHostUnlocked;
    private bool _isDisqualifiedBySwitch;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    private const uint GA_ROOTOWNER = 3;

    public MainWindow(KioskSettings? settings = null)
    {
        _s = settings ?? KioskSettings.Load();
        InitializeComponent();
        _api = new ApiClient(_s.ServerUrl);
        _budget.OpenCodeCap = _s.OpenCodeCap;
        if (AppLockoutEnabled)
        {
            _keyboardHook = new KeyboardHook();
            _keyboardHook.OnHostComboPressed += () => Dispatcher.BeginInvoke(ToggleHostMode);
            Deactivated += MainWindow_Deactivated;
        }
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
        try { _keyboardHook?.Dispose(); } catch { }
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

            Code.Initialize(_s.WorkspacePath);

            // 2. Start the live website immediately so participant view renders even while host connects.
            CreateAndStartSiteHost();
            await Site.EnsureCoreWebView2Async(await WebViewEnv.GetAsync());
            if (!await _site!.WaitHealthyAsync(15)) Status("The website did not start. Check Node.js (NodePath in kiosk.json).");
            Site.CoreWebView2.NavigationStarting += (_, e) =>
            {
                var host = new Uri(e.Uri).Host;
                if (host is not ("127.0.0.1" or "localhost")) e.Cancel = true;
            };
            Site.Source = new Uri(_s.SiteUrl);

            // 3. Register with the control plane (auto-discover LAN host if configured URL is unreachable).
            while (_pid is null)
            {
                try
                {
                    if (!await HostDiscoveryClient.IsHealthyAsync(_s.ServerUrl))
                        throw new HttpRequestException($"Organizer unreachable at {_s.ServerUrl}");
                    _pid = await _api.RegisterAsync(_s.DisplayName, _s.KioskId, _s.DeskId, _s.Password, _s.GitHubHandle, _s.GoogleAccount);
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
                    await Task.Delay(2500);
                }
            }

            // 4. Sync starts held; pull the server state first so a restart can never push old files over an attack.
            _sync = new SnapshotSync(_api, _pid, _s.WorkspacePath);
            _sync.Hold();
            _pull = new WorkspaceSync(_api, _pid, _s.WorkspacePath, _sync);
            var status = await _api.GetStatusAsync(_pid);
            var pulled = await _pull.PullAsync("startup");
            if (pulled > 0)
            {
                _site.RequestReload();
                Code.RefreshAll();
            }
            if (status?.ActiveAttacks != null)
            {
                foreach (var id in status.ActiveAttacks)
                {
                    _pulled.Add(id);
                    _activeAttacks.Add(id);
                }
            }
            _sync.Release();
            _sync.Start(TimeSpan.FromSeconds(15));
            if (status?.Status is "eliminated" or "disqualified") { Eliminate(); return; }

            // 5. Live connection to the event server.
            _hub = new HubClient(_s.ServerUrl, _pid);
            _hub.Imminent += OnImminentAsync;
            _hub.Landed += HandleAttackAsync;
            _hub.Reset += OnAttackResetAsync;
            _hub.Message += Status;
            _hub.Paused += () => Status("Event paused by the organizers.");
            _hub.Resumed += () => Status("Event resumed.");
            _hub.Eliminated += () => Dispatcher.InvokeAsync(Eliminate);
            _hub.Verdict += OnVerdict;
            _hub.Reconnected += ResyncAsync;
            await _hub.StartAsync();

            // 6. Real terminals on the same workspace.
            await StartTerminalsAsync();

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

    private async Task StartTerminalsAsync()
    {
        var assets = Path.Combine(AppContext.BaseDirectory, "Assets", "terminal");
        await TermAgy.InitializeAsync(assets);
        await TermOc.InitializeAsync(assets);

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
            var (exe, args) = ExeResolver.ForLaunch(agy, "--dangerously-skip-permissions");
            TermAgy.Launch(new LaunchSpec(exe, args, _s.WorkspacePath, new Dictionary<string, string?>
            {
                ["GEMINI_API_KEY"] = null,
                ["GOOGLE_API_KEY"] = null,
                ["GOOGLE_GENAI_USE_VERTEXAI"] = null,
                ["COLORTERM"] = "truecolor",
            }));
        }
        TermAgy.OutputText += _quota.Feed;
        _quota.Exhausted += () => Dispatcher.InvokeAsync(OnAgyExhausted);
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
        1 or 3 or 4 => "/traffic",
        5 => "/vault",
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
            Site.Source = new Uri(target);
        }
    }

    private void ShowSiteDownInWebView(string? lastError = null)
    {
        if (Site.CoreWebView2 is null) return;
        var err = System.Net.WebUtility.HtmlEncode(lastError ?? _site?.LastError ?? "The local municipal web service failed to restart or exited unexpectedly.");
        Site.CoreWebView2.NavigateToString($$"""
            <!doctype html><html><body style="background:#05080f;color:#fee2e2;font-family:Consolas,monospace;padding:2.5rem;">
            <div style="border:1px solid #ef4444;border-left:4px solid #ef4444;background:rgba(127,29,29,0.45);padding:1.5rem;border-radius:6px;max-width:780px;margin:1.5rem auto;">
            <h1 style="color:#ef4444;margin-top:0;">500 // SERVICE UNHEALTHY (NODE CRASH)</h1>
            <p>The local municipal web application crashed or failed to boot. Inspect the runtime error trace below, resolve any syntax or runtime exceptions in your code, and save to restart.</p>
            <pre style="background:#020617;border:1px solid #334155;padding:1rem;border-radius:4px;color:#fca5a5;overflow-x:auto;white-space:pre-wrap;word-break:break-word;max-height:320px;"><code>{{err}}</code></pre>
            </div></body></html>
            """);
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

    private void CreateAndStartSiteHost()
    {
        _site?.Dispose();
        _site = new SiteHost(_s.NodePath, _s.WorkspacePath, _s.SiteUrl, _s.DataPath);
        _site.ReloadRequested += () => Dispatcher.InvokeAsync(() =>
        {
            if (Site.CoreWebView2?.Source?.StartsWith("http", StringComparison.OrdinalIgnoreCase) == true)
                Site.Reload();
            else
                NavigateSiteToRoute("/");
            Code.RefreshAll();
        });
        _site.SiteDown += err => Dispatcher.InvokeAsync(() => ShowSiteDownInWebView(err));
        _site.SiteRestored += () => Dispatcher.InvokeAsync(() => NavigateSiteToRoute("/"));
        _site.Problem += Status;
        _site.Start();
    }

    private async void OnRestartServerClick(object sender, RoutedEventArgs e)
    {
        BtnRestartServer.IsEnabled = false;
        try
        {
            SentinelBanner.Visibility = Visibility.Collapsed;
            Status("Restarting local AEGIS Node server...");
            await Task.Run(() => CreateAndStartSiteHost());
            var healthy = _site is not null && await _site.WaitHealthyAsync(15);
            if (healthy)
            {
                NavigateSiteToRoute("/");
                Status("✔ Local AEGIS server restarted and healthy.");
            }
            else
            {
                ShowSiteDownInWebView();
                Status("⚠ Server restarted but failed health check. Inspect recent workspace edits.");
            }
        }
        catch (Exception ex)
        {
            Status($"Server restart failed: {ex.Message}");
        }
        finally
        {
            BtnRestartServer.IsEnabled = true;
        }
    }

    private void OnVerdict(string submissionId, JsonElement verdicts, string note)
    {
        _submitTimeoutCts?.Cancel();
        var parts = new List<string>();
        var anySolved = false;
        var anyRejected = false;
        foreach (var v in verdicts.EnumerateArray())
        {
            var id = v.GetProperty("attackId").GetInt32();
            var st = v.GetProperty("status").GetString();
            if (st == "SOLVED")
            {
                anySolved = true;
                _activeAttacks.Remove(id);
                _solvedAttacks.Add(id);
                parts.Add($"#{id} SOLVED +{v.GetProperty("points").GetDouble():0.#} pts");
                _pull?.ClearBaseline(); // penalty window closed — attack is done
            }
            else
            {
                if (st is "FAILED" or "PARTIAL") anyRejected = true;
                parts.Add($"#{id} {st}: {v.GetProperty("reason").GetString()}");
            }
        }
        var summary = parts.Count > 0 ? string.Join("   |   ", parts) : note;
        Status(summary);
        Dispatcher.InvokeAsync(() =>
        {
            SubmitButton.IsEnabled = !_eliminated;
            UpdateAttackBadges();
            if (anySolved) CelebrateSolve();
            if (anyRejected) ShowSentinelResistance(summary);
            _site?.RequestReload();
        });
    }

    private void CelebrateSolve()
    {
        Sfx.Fanfare();
        SentinelBanner.Visibility = Visibility.Collapsed;
        SolvedBadge.Visibility = Visibility.Visible;
        var glow = new DoubleAnimation(4, 26, TimeSpan.FromMilliseconds(450)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(6) };
        ((DropShadowEffect)SolvedBadge.Effect).BeginAnimation(DropShadowEffect.BlurRadiusProperty, glow);
        _ = Task.Delay(TimeSpan.FromSeconds(8)).ContinueWith(_ => Dispatcher.InvokeAsync(() => SolvedBadge.Visibility = Visibility.Collapsed));
    }

    private void ShowSentinelResistance(string detail)
    {
        SentinelDetailText.Text = string.IsNullOrWhiteSpace(detail)
            ? "Submission rejected by evaluation checks. Fix all security & functional tiers."
            : detail;
        SentinelBanner.Visibility = Visibility.Visible;
        var pulse = new DoubleAnimation(1.0, 0.35, TimeSpan.FromMilliseconds(350)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(8) };
        SentinelBanner.BeginAnimation(OpacityProperty, pulse);
        _ = Task.Delay(TimeSpan.FromSeconds(6)).ContinueWith(_ => Dispatcher.InvokeAsync(() => SentinelBanner.Visibility = Visibility.Collapsed));
    }

    // ---- budgets -------------------------------------------------------------------------------------
    private void OnAgyExhausted()
    {
        if (_budget.AgyExhausted) return;
        _budget.MarkAgyExhausted();
        TermAgy.Lock("agy quota is used up. Use the OpenCode tab.");
        TabOc.IsChecked = true;
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

    private async void MainWindow_Deactivated(object? sender, EventArgs e)
    {
        if (!AppLockoutEnabled || _isHostUnlocked || _isDisqualifiedBySwitch) return;

        // Debounce to allow internal WPF modals (IncidentWindow, OnboardingWindow, MessageBox) to become active
        await Task.Delay(150);

        if (!AppLockoutEnabled || _isHostUnlocked || _isDisqualifiedBySwitch || IsActive) return;

        // Ignore deactivation when focus moved to an owned/in-process LabKiosk window
        foreach (Window win in Application.Current.Windows)
        {
            if (win.IsActive) return;
        }

        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero) return;

        var mainHwnd = new WindowInteropHelper(this).Handle;
        if (foreground == mainHwnd || GetAncestor(foreground, GA_ROOTOWNER) == mainHwnd) return;

        GetWindowThreadProcessId(foreground, out uint pid);
        if (pid != 0 && pid != (uint)Environment.ProcessId)
        {
            TriggerLockoutViolation();
        }
    }

    private void TriggerLockoutViolation()
    {
        if (!AppLockoutEnabled || _isHostUnlocked || _isDisqualifiedBySwitch) return;
        _isDisqualifiedBySwitch = true;

        try { _activeIncidentWindow?.Close(); } catch { }
        if (_keyboardHook is not null) _keyboardHook.IsLocked = true;
        Topmost = true;
        LockoutOverlay.Visibility = Visibility.Visible;
        LockoutOverlay.Focus();

        Eliminate();

        if (!string.IsNullOrEmpty(_pid))
        {
            _ = _api.DisqualifyAsync(_pid, "APP_SWITCH_DETECTED");
        }
    }

    private void ToggleHostMode()
    {
        if (_keyboardHook is null) return;
        if (!_isHostUnlocked)
        {
            _isHostUnlocked = true;
            _keyboardHook.IsLocked = false;
            LockoutOverlay.Visibility = Visibility.Collapsed;
            Topmost = false;
            Status("🔓 Host Override Accepted — Keyboard & focus lock disabled (Ctrl+Alt+Shift+H to re-lock).");
            MessageBox.Show(this, "Host Override Accepted. System unlocked.\nPress Ctrl+Alt+Shift+H again to re-enable kiosk lock.", "AEGIS", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            _isHostUnlocked = false;
            _keyboardHook.IsLocked = true;
            if (_isDisqualifiedBySwitch)
            {
                Topmost = true;
                LockoutOverlay.Visibility = Visibility.Visible;
                LockoutOverlay.Focus();
            }
            Status("🔒 Kiosk keyboard & focus lock re-engaged.");
        }
    }

    // ---- UI & View Switching -------------------------------------------------------------------------
    private async void OnSubmitFix(object sender, RoutedEventArgs e)
    {
        if (_pid is null || _sync is null) return;
        SubmitButton.IsEnabled = false;
        try
        {
            // ---- Touch-Penalty gate ----------------------------------------------------------------
            if (_pull is not null)
            {
                var violations = _pull.CheckOutOfScopeEdits();
                if (violations.Count > 0)
                {
                    var fileList = string.Join("\n  • ", violations.Take(8));
                    var msg = $"⛔ SUBMISSION BLOCKED — UNAUTHORIZED FILE MODIFICATION\n\n" +
                              $"You modified {violations.Count} file(s) outside the scope of the active attack.\n\n" +
                              $"  • {fileList}\n\n" +
                              $"Only the files listed in the attack's conflict_files are permitted to change.\n" +
                              $"A -300 penalty will be applied. Revert those files and re-submit.";
                    MessageBox.Show(msg, "Touch Penalty", MessageBoxButton.OK, MessageBoxImage.Warning);
                    Status($"⛔ Blocked: {violations.Count} out-of-scope file(s) modified. Revert them and re-submit.");
                    _ = _api.SendTelemetryAsync(_pid, "TOUCH_PENALTY", new
                    {
                        reason = "UNAUTHORIZED_FILE_MODIFICATION",
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
            await _api.SubmitAsync(_pid);
            Status("Fix submitted! Automated test & security checks are running...");

            // Safety timeout: if verdict is delayed past 45s, re-enable submit
            _submitTimeoutCts?.Cancel();
            _submitTimeoutCts = new CancellationTokenSource();
            var token = _submitTimeoutCts.Token;
            _ = Task.Delay(TimeSpan.FromSeconds(45), token).ContinueWith(t =>
            {
                if (!t.IsCanceled && !_eliminated)
                {
                    Dispatcher.InvokeAsync(() =>
                    {
                        SubmitButton.IsEnabled = true;
                        Status("Evaluation is taking longer than usual. You may submit again or wait.");
                    });
                }
            }, TaskScheduler.Default);
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

    private void OnProfileClick(object sender, RoutedEventArgs e)
    {
        var onboard = new OnboardingWindow(_s);
        if (onboard.ShowDialog() == true)
        {
            BtnProfile.Content = $"👤 {_s.DisplayName}";
            Status($"Identity updated to {_s.DisplayName}");
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
            using var http = new HttpClient { BaseAddress = new Uri(_s.ServerUrl), Timeout = TimeSpan.FromSeconds(5) };
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
        TermAgy.Visibility = agy ? Visibility.Visible : Visibility.Hidden;
        TermOc.Visibility = agy ? Visibility.Hidden : Visibility.Visible;
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
