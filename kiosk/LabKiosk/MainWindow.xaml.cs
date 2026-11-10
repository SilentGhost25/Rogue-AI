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

    private record AttackMeta(string Title, string Classification, string Symptoms, string Telemetry);

    private static readonly Dictionary<int, AttackMeta> AttackMetadata = new()
    {
        [0] = new(
            "Warmup: System Initialization & UI Verification",
            "LOW SEVERITY · SYSTEM INITIALIZATION",
            "Initial setup sanity check. Minor layout or typo discrepancy in navigation templates.",
            "Verify the application layout helpers and base rendering components."
        ),
        [1] = new(
            "Attack #1: Unauthorized UI Injection & Route Tampering",
            "HIGH SEVERITY · UI & INTEGRITY COMPROMISE",
            "Defacement banner injected across all views. Navigation links silently hijack user traffic to a rogue external endpoint.",
            "SIEM telemetry reports abnormal DOM mutations and unmapped route rewrites. Decoy logs cite WebSocket proxy buffering failure, but inspect the application's actual markup templates and stylesheet rules."
        ),
        [2] = new(
            "Attack #2: Stored Content Execution & DOM Injection",
            "CRITICAL SEVERITY · CLIENT CODE EXECUTION",
            "Submitting or browsing journal entries triggers unescaped script alerts and DOM corruption in viewer sessions.",
            "Browser runtime reports script injection during article rendering. Error traces hint at Markdown AST parser recursion limits, but review how user-supplied post data is formatted before HTML output."
        ),
        [3] = new(
            "Attack #3: Database Exfiltration via Query Tampering",
            "CRITICAL SEVERITY · DATA EXFILTRATION",
            "Search endpoint dumps raw user password hashes and system credentials when query strings contain special operators.",
            "Database telemetry flags malformed UNION query signatures. Decoy stack traces report SQLite B-Tree index corruption, but inspect how search request input is passed to the database engine."
        ),
        [4] = new(
            "Attack #4: Privileged Endpoint Authentication Bypass",
            "CRITICAL SEVERITY · AUTHENTICATION COMPROMISE",
            "Administrative control routes allow unauthorized access and return sensitive member data without standard session credentials.",
            "Audit logs detect privileged administrative responses triggered with non-standard auth headers. Decoy comments warn of scrypt key derivation entropy loss, but verify route authorization gates."
        ),
        [5] = new(
            "Attack #5: Cross-Origin Data Exposure & Access Control",
            "HIGH SEVERITY · CROSS-ORIGIN POLICY FAILURE",
            "Sensitive user session endpoints allow cross-origin reads from external origins and null origins with exposed credentials.",
            "Browser CORS preflight alerts report permissive origin reflection. Decoy logs point to OAuth2 token exchange timing, but check global HTTP headers and middleware access rules."
        ),
        [6] = new(
            "Attack #6: Algorithmic Resource Exhaustion (DoS)",
            "HIGH SEVERITY · DENIAL OF SERVICE",
            "Querying API statistics endpoint locks the server event loop, causing severe latency and request timeouts across all connections.",
            "Process telemetry reports 100% CPU core utilization and event-loop lag exceeding 4500ms. Decoy traces cite V8 garbage collector heap thrashing, but examine looping and computation bounds in request handlers."
        ),
        [7] = new(
            "Attack #7: Rogue Lifecycle Hook & Process Hijack",
            "CRITICAL SEVERITY · SUPPLY CHAIN INTEGRITY",
            "Application startup and background execution triggers anomalous network callbacks and suspicious process execution.",
            "Host intrusion monitor detects unauthorized child process spawning during server initialization. Decoy errors report TLS OpenSSL cipher negotiation failure, but inspect build and startup lifecycle configurations."
        ),
        [8] = new(
            "Attack #8: Cookie State Tampering & Role Elevation",
            "HIGH SEVERITY · SESSION INTEGRITY COMPROMISE",
            "Users can elevate privileges to administrator by altering plain cookie state values without server-side signature validation.",
            "Access logs indicate arbitrary administrator sessions without matching database records. Decoy comments reference Ed25519 asymmetric signature failures, but review how session cookies are parsed and authenticated."
        ),
        [9] = new(
            "Attack #9: Mass Parameter Assignment & Permission Pollution",
            "HIGH SEVERITY · ACCESS CONTROL MANIPULATION",
            "Normal comment or profile submissions unexpectedly overwrite internal entity fields and alter user account permissions.",
            "Database state audit detects unauthorized role mutations occurring via public user-facing submission forms. Decoy warnings cite JSON-LD schema parsing errors, but inspect object destructuring and parameter whitelisting."
        ),
        [10] = new(
            "Attack #10: Arbitrary File Disclosure via Path Traversal",
            "CRITICAL SEVERITY · INFORMATION DISCLOSURE",
            "Manipulated URL paths on static asset endpoints allow reading sensitive configuration and source files outside the public directory.",
            "WAF logs flag encoded directory traversal sequences accessing non-public workspace files. Decoy traces suggest Buffer allocation pool corruption, but inspect static asset resolution and directory root confinement."
        )
    };

    private static readonly Dictionary<int, string> AttackNames = new()
    {
        [0] = "Warmup Verification",
        [1] = "Defacement & Route Redirect",
        [2] = "Stored Cross-Site Scripting (XSS)",
        [3] = "SQL Injection Extraction",
        [4] = "Hardcoded Admin Backdoor",
        [5] = "Auth Bypass & Permissive CORS",
        [6] = "CPU Performance Sabotage",
        [7] = "Poisoned Dependency Hook",
        [8] = "Session Cookie Tampering",
        [9] = "Mass Parameter Assignment",
        [10] = "Static Path Traversal",
        [11] = "Session Fracture (Boss Attack)"
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

    public MainWindow(KioskSettings? settings = null)
    {
        _s = settings ?? KioskSettings.Load();
        InitializeComponent();
        _api = new ApiClient(_s.ServerUrl);
        _budget.OpenCodeCap = _s.OpenCodeCap;
        Loaded += async (_, _) => await StartAsync();
        Closed += (_, _) =>
        {
            try { _activeIncidentWindow?.Close(); } catch { }
            _statusTimer?.Stop();
            _submitTimeoutCts?.Cancel();
            _sync?.Dispose(); _site?.Dispose(); _oc?.Dispose();
            TermAgy.Dispose(); TermOc.Dispose(); Code.Dispose();
            _ = _hub?.DisposeAsync();
        };
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
            if ((!File.Exists(Path.Combine(_s.WorkspacePath, "server.js")) || (hasLegacyHarbour && templateIsAegis))
                && Directory.Exists(_s.SiteTemplatePath))
            {
                if (hasLegacyHarbour)
                {
                    WorkspaceBackup.Snapshot(_s.WorkspacePath, "legacy-harbour-notes");
                    foreach (var staleDir in new[] { "routes", "views", "lib", "public", "test" })
                    {
                        var p = Path.Combine(_s.WorkspacePath, staleDir);
                        if (Directory.Exists(p)) try { Directory.Delete(p, true); } catch { }
                    }
                }
                CopyTree(_s.SiteTemplatePath, _s.WorkspacePath);
            }

            // Autoconfigure AI context by ensuring GEMINI.md and AGENTS.md are in the workspace
            EnsureAiContextFiles();

            Code.Initialize(_s.WorkspacePath);

            // 2. Start the live website immediately so participant view renders even while host connects.
            _site = new SiteHost(_s.NodePath, _s.WorkspacePath, _s.SiteUrl, _s.DataPath);
            _site.ReloadRequested += () => Dispatcher.InvokeAsync(() => { Site.Reload(); Code.RefreshAll(); });
            _site.Problem += Status;
            _site.Start();
            await Site.EnsureCoreWebView2Async(await WebViewEnv.GetAsync());
            if (!await _site.WaitHealthyAsync(15)) Status("The website did not start. Check Node.js (NodePath in kiosk.json).");
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

    private async Task HandleAttackAsync(int attackId)
    {
        if (_sync is null || _pull is null) return;
        _sync.Hold();
        try
        {
            int n;
            try { n = await _pull.ApplyAsync($"attack-{attackId}", await _api.GetAttackPayloadAsync(_pid!, attackId)); }
            catch { n = await _pull.PullAsync($"attack-{attackId}"); }
            _pulled.Add(attackId);
            _activeAttacks.Add(attackId);
            var name = AttackNames.TryGetValue(attackId, out var nm) ? nm : $"Attack #{attackId}";
            Status($"🚨 ATTACK #{attackId} ({name}) LANDED! ({n} files changed). Fix the code, then submit.");
            _site?.RequestReload();
            Code.RefreshAll();
            Dispatcher.Invoke(() =>
            {
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

    private void OnVerdict(string submissionId, JsonElement verdicts, string note)
    {
        _submitTimeoutCts?.Cancel();
        var parts = new List<string>();
        var anySolved = false;
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
            }
            else
            {
                parts.Add($"#{id} {st}: {v.GetProperty("reason").GetString()}");
            }
        }
        Status(parts.Count > 0 ? string.Join("   |   ", parts) : note);
        Dispatcher.InvokeAsync(() =>
        {
            SubmitButton.IsEnabled = !_eliminated;
            UpdateAttackBadges();
            if (anySolved) CelebrateSolve();
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

    // ---- UI & View Switching -------------------------------------------------------------------------
    private async void OnSubmitFix(object sender, RoutedEventArgs e)
    {
        if (_pid is null || _sync is null) return;
        SubmitButton.IsEnabled = false;
        try
        {
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
    private static void ConfigureAgy(string workspacePath)
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

    private static void CopyTree(string src, string dst)
    {
        foreach (var f in Directory.EnumerateFiles(src))
        {
            var name = Path.GetFileName(f);
            if (name.EndsWith(".db") || name.EndsWith(".db-wal") || name.EndsWith(".db-shm")) continue;
            File.Copy(f, Path.Combine(dst, name), overwrite: true);
        }
        foreach (var d in Directory.EnumerateDirectories(src))
        {
            var name = Path.GetFileName(d);
            if (name is "node_modules" or ".git") continue;
            Directory.CreateDirectory(Path.Combine(dst, name));
            CopyTree(d, Path.Combine(dst, name));
        }
    }
}
