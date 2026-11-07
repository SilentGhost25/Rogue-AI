using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using LabKiosk.Services;

namespace LabKiosk;

public partial class MainWindow : Window
{
    private readonly KioskSettings _settings = KioskSettings.Load();
    private KioskConfig _config;
    private ApiClient _api;
    private readonly BudgetState _budget = new();
    private bool _uiInitialized = false;
    private HubClient? _hub;
    private SnapshotSync? _sync;
    private TelemetryCollector? _telemetry;
    private ClockSync? _clock;
    private string? _participantId;
    private int? _lastAppliedAttackId;
    private AttackPayloadDto? _bufferedPayload;
    private int? _bufferedAttackId;
    private CancellationTokenSource? _landingTimerCts;

    public MainWindow() : this(KioskConfig.FromArgs(Environment.GetCommandLineArgs()))
    {
    }

    public MainWindow(KioskConfig config)
    {
        _config = config;
        _settings.ServerUrl = _config.ServerUrl;
        if (!string.IsNullOrEmpty(_config.DeskId))
        {
            _settings.KioskId = _config.DeskId;
            _settings.DisplayName = _config.DeskId;
        }

        InitializeComponent();
        _api = new ApiClient(_config.ServerUrl);
        if (!string.IsNullOrEmpty(_config.DeskToken))
        {
            _api.SetToken(_config.DeskToken);
        }

        Loaded += async (_, _) => await StartAsync();
        Closed += (_, _) =>
        {
            _landingTimerCts?.Cancel();
            _sync?.Dispose();
            _telemetry?.Dispose();
            CodeView?.Dispose();
            _ = _clock?.DisposeAsync();
            _ = _hub?.DisposeAsync();
            try { _localSiteProc?.Kill(entireProcessTree: true); } catch { }
        };

        // F5 shortcut for Submit Fix, F6 for Test Locally
        KeyDown += (s, e) =>
        {
            if (e.Key == System.Windows.Input.Key.F5)
            {
                OnSubmitFix(SubmitButton, new RoutedEventArgs());
            }
            else if (e.Key == System.Windows.Input.Key.F6)
            {
                OnTestLocally(TestLocallyButton, new RoutedEventArgs());
            }
        };
    }

    private async Task StartAsync()
    {
        if (!_uiInitialized)
        {
            _uiInitialized = true;

            // Prompt participant login on startup
            if (string.IsNullOrWhiteSpace(_settings.DisplayName) ||
                _settings.DisplayName == Environment.MachineName ||
                string.IsNullOrWhiteSpace(_settings.Password))
            {
                var loginDlg = new ParticipantLoginDialog(_settings) { Owner = this };
                if (loginDlg.ShowDialog() == true)
                {
                    _settings.DisplayName = loginDlg.ParticipantName;
                    _settings.DeskId = loginDlg.DeskId;
                    _settings.Password = loginDlg.Password;
                    _settings.ServerUrl = loginDlg.ServerUrl;
                    _config.ServerUrl = loginDlg.ServerUrl;
                    _config.DeskId = loginDlg.DeskId;
                    _api = new ApiClient(_config.ServerUrl);
                }
            }
            UpdateUserButton();

            try
            {
                Directory.CreateDirectory(_settings.WorkspacePath);
            }
            catch
            {
                _settings.WorkspacePath = Path.Combine(AppContext.BaseDirectory, "workspace");
                Directory.CreateDirectory(_settings.WorkspacePath);
            }

            EnsureWorkspaceSeeded();
            EnsureLocalSiteRunning();

            // 1. Initialize WebView2 for the live website with isolated profile folder
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var udata = Path.Combine(localAppData, "RogueAI", "WV2_Kiosk_" + Environment.ProcessId);
            var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(null, udata);
            await Site.EnsureCoreWebView2Async(env);

            Site.CoreWebView2.NavigationStarting += (s, e) =>
            {
                if (!e.Uri.StartsWith("http://127.0.0.1:3000", StringComparison.OrdinalIgnoreCase) &&
                    !e.Uri.StartsWith("http://localhost:3000", StringComparison.OrdinalIgnoreCase) &&
                    !e.Uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase) &&
                    !e.Uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
                {
                    e.Cancel = true;
                    Site.CoreWebView2.NavigateToString(@"
                        <!doctype html>
                        <html>
                        <head><meta charset='utf-8'></head>
                        <body style='margin:0; background:#0b0f19; color:#f87171; font-family:-apple-system,BlinkMacSystemFont,Segoe UI,Roboto,sans-serif; display:flex; align-items:center; justify-content:center; min-height:100vh; text-align:center;'>
                          <div style='max-width:540px; margin:24px; padding:32px; background:#1e293b; border:2px solid #ef4444; border-radius:12px; box-shadow:0 10px 25px rgba(0,0,0,0.5);'>
                            <div style='font-size:48px; margin-bottom:12px;'>🚨</div>
                            <h2 style='color:#ef4444; margin:0 0 12px 0; font-size:22px; font-weight:700;'>MALICIOUS BEACON BLOCKED</h2>
                            <p style='color:#cbd5e1; font-size:14px; line-height:1.5; margin-bottom:16px;'>Your web application was compromised by an intrusion payload and attempted to redirect the browser to an external address:</p>
                            <div style='background:#0f172a; padding:10px 14px; border-radius:6px; font-family:Consolas,monospace; font-size:13px; color:#38bdf8; word-break:break-all; margin-bottom:20px; border:1px solid #334155;'>" + System.Net.WebUtility.HtmlEncode(e.Uri) + @"</div>
                            <p style='color:#94a3b8; font-size:13px; margin:0 0 20px 0;'>Inspect and remove this malicious script from <code>lib/html.js</code> or <code>routes/search.js</code> using the in-app code editor, then click <b>Reload</b>.</p>
                            <button onclick='window.location.href=""http://127.0.0.1:3000/?nobounce=1""' style='background:#ef4444; color:#fff; border:none; padding:10px 20px; border-radius:6px; font-weight:600; cursor:pointer;'>View Site (Safe Mode: ?nobounce)</button>
                          </div>
                        </body>
                        </html>");
                }
            };

            Site.NavigationCompleted += (s, e) =>
            {
                if (!e.IsSuccess)
                {
                    var failedUri = Site.Source?.ToString() ?? "";
                    if (failedUri.StartsWith(_settings.SiteUrl, StringComparison.OrdinalIgnoreCase))
                    {
                        Dispatcher.InvokeAsync(() =>
                        {
                            StatusText.Text = $"⚠ Website at {_settings.SiteUrl} unreachable ({e.WebErrorStatus}). Verify background node server.";
                            StatusText.Foreground = (System.Windows.Media.SolidColorBrush)FindResource("Alert");
                        });
                    }
                }
            };
            Site.Source = new Uri(_settings.SiteUrl);

            // 2. Initialize in-app Code Viewer
            CodeView.InitializeWorkspace(_settings.WorkspacePath);

            // 3. Initialize ConPTY Terminals
            InitializeTerminals();
        }

        await ConnectToControlPlaneAsync();
    }

    private async Task ConnectToControlPlaneAsync()
    {
        try
        {
            StatusText.Text = $"Locating Breach Command ({_config.ServerUrl})...";
            StatusText.Foreground = (System.Windows.Media.SolidColorBrush)FindResource("Ink");

            // 1. Check if configured server is reachable; if not, auto-discover dynamically on the network
            var isReachable = await NetworkDiscovery.ProbeHostAsync(_config.ServerUrl, 800);
            if (!isReachable)
            {
                StatusText.Text = "Breach Command unreachable. Auto-detecting host on local network...";
                StatusText.Foreground = (System.Windows.Media.SolidColorBrush)FindResource("Alert");

                var discovered = await NetworkDiscovery.AutoDiscoverHostAsync(_config.ServerUrl, status =>
                {
                    Dispatcher.InvokeAsync(() => StatusText.Text = status);
                });

                if (!string.IsNullOrEmpty(discovered))
                {
                    _config.ServerUrl = discovered;
                    _settings.ServerUrl = discovered;
                    _settings.Save();
                    _api = new ApiClient(_config.ServerUrl);
                    StatusText.Text = $"Breach Command located dynamically at {_config.ServerUrl}!";
                    StatusText.Foreground = (System.Windows.Media.SolidColorBrush)FindResource("Ok");
                    await Task.Delay(500);
                }
            }

            StatusText.Text = $"Connecting to Breach Command ({_config.ServerUrl})...";
            StatusText.Foreground = (System.Windows.Media.SolidColorBrush)FindResource("Ink");

            // Stop existing sync or connection if retrying
            _sync?.Dispose();
            _sync = null;
            _telemetry?.Dispose();
            _telemetry = null;
            if (_hub != null)
            {
                try { await _hub.DisposeAsync(); } catch { }
                _hub = null;
            }

            // 4. Enroll or Register with the event server
            if (!string.IsNullOrEmpty(_config.DeskToken))
            {
                var (pid, desk) = await _api.EnrollAsync(_config.DeskToken, _config.DeskId, _config.DeskId);
                _participantId = pid;
            }
            else
            {
                _participantId = await _api.RegisterAsync(_settings.DisplayName, _settings.KioskId, _settings.DeskId, _settings.Password, _settings.GitHubHandle, _settings.GoogleAccount);
            }

            // 5. Start continuous snapshot sync (every 15s), telemetry collector, and clock sync
            _clock = new ClockSync(_api);
            _sync = new SnapshotSync(_api, _participantId, _settings.WorkspacePath);
            _sync.Start(TimeSpan.FromSeconds(15));
            _telemetry = new TelemetryCollector(_api, _participantId, _settings.DeskId ?? "DESK");
            _telemetry.Start(TimeSpan.FromSeconds(20));

            // 6. Connect to SignalR Hub
            _hub = new HubClient(_config.ServerUrl, _participantId, _api.Token ?? _config.DeskToken ?? "");

            _hub.Imminent += async (attackId, seconds, landAtUtcStr) =>
            {
                Dispatcher.InvokeAsync(() => Sfx.Siren());
                await Dispatcher.InvokeAsync(() =>
                {
                    StatusText.Text = $"⚠ IMMINENT BREACH #{attackId} in {seconds}s! Saving workspace & pre-buffering payload...";
                    StatusText.Foreground = (System.Windows.Media.SolidColorBrush)FindResource("Alert");
                });

                // Auto-save local workspace before landing
                await _sync.FlushAsync();
                await _hub.AckImminentAsync(attackId);

                // Pre-fetch attack payload into memory during the countdown window
                if (_participantId is not null)
                {
                    try
                    {
                        var payload = await _api.GetAttackPayloadAsync(_participantId, attackId);
                        if (payload?.Files != null && payload.Files.Count > 0)
                        {
                            _bufferedPayload = payload;
                            _bufferedAttackId = attackId;
                        }
                    }
                    catch (Exception ex)
                    {
                        await Dispatcher.InvokeAsync(() => StatusText.Text = $"Payload pre-fetch warning: {ex.Message}");
                    }
                }

                // Compute exact local delay until server landAtUtc
                var landAt = DateTime.UtcNow.AddSeconds(seconds);
                if (!string.IsNullOrEmpty(landAtUtcStr) && DateTime.TryParse(landAtUtcStr, out var parsedLandAt))
                {
                    landAt = parsedLandAt.ToUniversalTime();
                }

                var now = _clock?.SyncedUtcNow ?? DateTime.UtcNow;
                var delay = landAt - now;

                _landingTimerCts?.Cancel();
                _landingTimerCts = new CancellationTokenSource();
                var ct = _landingTimerCts.Token;

                _ = Task.Run(async () =>
                {
                    if (delay > TimeSpan.Zero)
                    {
                        try { await Task.Delay(delay, ct); } catch (OperationCanceledException) { return; }
                    }
                    await Dispatcher.InvokeAsync(() => ApplyAttack(attackId));
                }, ct);
            };

            _hub.Landed += attackId => Dispatcher.InvokeAsync(() =>
            {
                // Instant write or fallback if timer hasn't fired yet
                ApplyAttack(attackId);
            });

            _hub.Reconnected += async () =>
            {
                await ReconcileActiveBreachAsync();
            };

            // Initial recovery check in case kiosk booted while breach was active/imminent
            _ = Task.Run(async () => await ReconcileActiveBreachAsync());

            _hub.Verdict += (attackId, status, reason, score) => Dispatcher.InvokeAsync(() =>
            {
                if (status == "SOLVED")
                {
                    Sfx.Fanfare();
                    VerdictBadge.Visibility = Visibility.Visible;
                    VerdictBadge.Background = (System.Windows.Media.SolidColorBrush)FindResource("Ok");
                    VerdictBadgeText.Text = "SOLVED";
                    StatusText.Text = $"Attack #{attackId} NEUTRALIZED! +{score:F1} pts. ({reason})";
                    StatusText.Foreground = (System.Windows.Media.SolidColorBrush)FindResource("Ok");
                }
                else
                {
                    VerdictBadge.Visibility = Visibility.Visible;
                    VerdictBadge.Background = (System.Windows.Media.SolidColorBrush)FindResource("Alert");
                    VerdictBadgeText.Text = status;
                    StatusText.Text = $"Submission rejected ({status}): {reason}";
                    StatusText.Foreground = (System.Windows.Media.SolidColorBrush)FindResource("Alert");
                }
                ReloadSiteSafe();
            });

            _hub.Message += text => Dispatcher.InvokeAsync(() => StatusText.Text = text);
            _hub.Paused += () => Dispatcher.InvokeAsync(() =>
            {
                StatusText.Text = "⏸ Event PAUSED by the organizers. Timers frozen.";
                StatusText.Foreground = (System.Windows.Media.SolidColorBrush)FindResource("Mute");
            });

            await _hub.StartAsync();

            StatusText.Text = "Connected to Breach Command. System nominal. No active attacks.";
            StatusText.Foreground = (System.Windows.Media.SolidColorBrush)FindResource("Ink");
            RefreshMeters();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Server unreachable ({_config.ServerUrl}): {ex.Message}";
            StatusText.Foreground = (System.Windows.Media.SolidColorBrush)FindResource("Alert");

            // Prompt user to confirm or update organizer host IP
            Dispatcher.InvokeAsync(async () =>
            {
                var dlg = new ServerConfigDialog(_config.ServerUrl, _config.DeskId) { Owner = this };
                if (dlg.ShowDialog() == true)
                {
                    _config.ServerUrl = dlg.ServerUrl;
                    _config.DeskId = dlg.DeskId;
                    _settings.ServerUrl = dlg.ServerUrl;
                    _settings.DeskId = dlg.DeskId;
                    _settings.Save();
                    _api = new ApiClient(_config.ServerUrl);
                    await ConnectToControlPlaneAsync();
                }
            });
        }
    }

    private void ApplyAttack(int attackId)
    {
        if (_lastAppliedAttackId == attackId) return; // Prevent duplicate writes in-memory

        var markerDir = Path.Combine(_settings.WorkspacePath, ".breach-applied");
        var markerFile = Path.Combine(markerDir, $"attack-{attackId}.applied");
        if (File.Exists(markerFile))
        {
            _lastAppliedAttackId = attackId;
            return; // Already applied to disk previously; NEVER overwrite participant fix!
        }

        _lastAppliedAttackId = attackId;
        _landingTimerCts?.Cancel();

        StatusText.Text = $"🔴 Attack #{attackId} has LANDED! Applying attack payload...";
        StatusText.Foreground = (System.Windows.Media.SolidColorBrush)FindResource("Alert");
        VerdictBadge.Visibility = Visibility.Collapsed;

        var payload = (_bufferedAttackId == attackId) ? _bufferedPayload : null;
        if (payload == null && _participantId != null)
        {
            try
            {
                payload = _api.GetAttackPayloadAsync(_participantId, attackId).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Payload download warning: {ex.Message}";
            }
        }

        if (payload?.Files != null && payload.Files.Count > 0)
        {
            var backupDir = Path.Combine(_settings.WorkspacePath, ".breach-backup", attackId.ToString());
            Directory.CreateDirectory(backupDir);
            foreach (var file in payload.Files)
            {
                var targetPath = Path.Combine(_settings.WorkspacePath, file.Path);
                if (File.Exists(targetPath))
                {
                    var bkp = Path.Combine(backupDir, file.Path);
                    Directory.CreateDirectory(Path.GetDirectoryName(bkp)!);
                    File.Copy(targetPath, bkp, overwrite: true);
                }
                var bytes = Convert.FromBase64String(file.ContentBase64);
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                File.WriteAllBytes(targetPath, bytes);
            }
        }

        // Write durable marker so reboot / reconnect never overwrites code
        try
        {
            Directory.CreateDirectory(markerDir);
            File.WriteAllText(markerFile, DateTime.UtcNow.ToString("O"));
        }
        catch { }

        var isSynced = _clock != null && _clock.IsSynced;
        var clockSource = isSynced ? "SYNCED" : "UNSYNCED_FALLBACK";
        var now = isSynced ? _clock!.SyncedUtcNow : DateTime.UtcNow;
        var landingTimestamp = now.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

        Console.WriteLine($"[SIMULTANEITY] Attack #{attackId} payload written at {landingTimestamp} (Clock: {clockSource})");
        System.Diagnostics.Debug.WriteLine($"[SIMULTANEITY] Attack #{attackId} payload written at {landingTimestamp} (Clock: {clockSource})");
        StatusText.Text = $"🔴 Attack #{attackId} has LANDED at {landingTimestamp}! (Clock: {clockSource}) Inspect code in viewer and neutralize the exploit.";

        // Refresh website and code viewer to show the corruption
        ReloadSiteSafe();
        CodeView.RefreshFileList();
        CodeView.ReloadCurrentFile();
    }

    private async Task ReconcileActiveBreachAsync()
    {
        if (_participantId == null) return;
        try
        {
            var breach = await _api.GetActiveBreachAsync(_participantId);
            if (breach?.ActiveAttackId != null)
            {
                var attackId = breach.ActiveAttackId.Value;
                if (_lastAppliedAttackId != attackId)
                {
                    if (breach.Landed)
                    {
                        await Dispatcher.InvokeAsync(() => ApplyAttack(attackId));
                    }
                    else if (DateTime.TryParse(breach.LandAtUtc, out var landAt))
                    {
                        var now = _clock?.SyncedUtcNow ?? DateTime.UtcNow;
                        var delay = landAt.ToUniversalTime() - now;
                        if (delay <= TimeSpan.Zero)
                        {
                            await Dispatcher.InvokeAsync(() => ApplyAttack(attackId));
                        }
                        else
                        {
                            var payload = await _api.GetAttackPayloadAsync(_participantId, attackId);
                            _bufferedPayload = payload;
                            _bufferedAttackId = attackId;

                            _landingTimerCts?.Cancel();
                            _landingTimerCts = new CancellationTokenSource();
                            var ct = _landingTimerCts.Token;
                            _ = Task.Run(async () =>
                            {
                                try { await Task.Delay(delay, ct); } catch (OperationCanceledException) { return; }
                                await Dispatcher.InvokeAsync(() => ApplyAttack(attackId));
                            }, ct);
                        }
                    }
                }
            }
        }
        catch { }
    }

    private void InitializeTerminals()
    {
        var bundledNodeDir = Path.Combine(AppContext.BaseDirectory, "nodejs");
        if (Directory.Exists(bundledNodeDir))
        {
            var currentPath = Environment.GetEnvironmentVariable("PATH") ?? "";
            if (!currentPath.Contains(bundledNodeDir, StringComparison.OrdinalIgnoreCase))
            {
                Environment.SetEnvironmentVariable("PATH", $"{bundledNodeDir};{currentPath}");
            }
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var agyExe = Path.Combine(localAppData, @"agy\bin\agy.exe");
        var agyCmd = Path.Combine(localAppData, @"agy\bin\agy.cmd");

        // Set up agy Terminal
        AgyTerm.WorkingDirectory = _settings.WorkspacePath;
        if (File.Exists(agyExe))
        {
            AgyTerm.StartupCommandLine = $"\"{agyExe}\"";
        }
        else if (File.Exists(agyCmd))
        {
            AgyTerm.StartupCommandLine = $"cmd.exe /c \"{agyCmd}\"";
        }
        else
        {
            AgyTerm.StartupCommandLine = "powershell.exe -NoExit -Command \"if (Get-Command agy -ErrorAction SilentlyContinue) { agy } else { Write-Host 'Antigravity CLI (agy) Workspace Ready' -ForegroundColor Cyan; Write-Host 'agy executable not detected in %LOCALAPPDATA%\\agy\\bin or PATH.' -ForegroundColor Yellow; Write-Host 'Run: .\\scripts\\provision-ai-clis.ps1 to configure the AI CLIs.' -ForegroundColor Gray; Write-Host ('Workspace: ' + (Get-Location)) -ForegroundColor Gray }\"";
        }

        // Set up OpenCode Terminal
        OpenCodeTerm.WorkingDirectory = _settings.WorkspacePath;
        OpenCodeTerm.StartupCommandLine = "powershell.exe -NoExit -Command \"if (Get-Command opencode -ErrorAction SilentlyContinue) { Write-Host 'Starting OpenCode interactive session...' -ForegroundColor Green; opencode } else { Write-Host 'OpenCode Terminal Workspace Ready' -ForegroundColor Green; Write-Host 'opencode not detected in PATH.' -ForegroundColor Yellow; Write-Host 'Run: .\\scripts\\provision-ai-clis.ps1 to install opencode globally.' -ForegroundColor Gray; Write-Host ('Workspace: ' + (Get-Location)) -ForegroundColor Gray }\"";

        // Listen for quota signals
        AgyTerm.ConPTYTerm.TerminalOutput += (s, e) =>
        {
            var text = e.Data;
            if (string.IsNullOrEmpty(text)) return;

            if (text.Contains("Individual quota reached", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("RESOURCE_EXHAUSTED", StringComparison.OrdinalIgnoreCase))
            {
                Dispatcher.InvokeAsync(() =>
                {
                    _budget.MarkAgyExhausted();
                    RefreshMeters();
                    StatusText.Text = "Antigravity quota exhausted! Switching to OpenCode for remaining tasks.";
                    StatusText.Foreground = (System.Windows.Media.SolidColorBrush)FindResource("Alert");
                    TabOpenCode.IsChecked = true;
                    OnTabChanged(TabOpenCode, new RoutedEventArgs());
                    if (_participantId is not null)
                    {
                        _ = _api.SendTelemetryAsync(_participantId, "AGY_EXHAUSTED");
                    }
                });
            }
        };
    }

    private async void OnSubmitFix(object sender, RoutedEventArgs e)
    {
        if (_participantId is null || _sync is null) return;
        SubmitButton.IsEnabled = false;
        try
        {
            StatusText.Text = "Uploading fix snapshot to Gitea...";
            StatusText.Foreground = (System.Windows.Media.SolidColorBrush)FindResource("Ink");
            await _sync.FlushAsync();
            await _api.SubmitAsync(_participantId);
            StatusText.Text = "Fix submitted to evaluator queue. Awaiting verdict...";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Submit failed: {ex.Message}. Please retry.";
            StatusText.Foreground = (System.Windows.Media.SolidColorBrush)FindResource("Alert");
        }
        finally
        {
            SubmitButton.IsEnabled = true;
        }
    }

    private void UpdateUserButton()
    {
        var label = !string.IsNullOrWhiteSpace(_settings.DisplayName) && _settings.DisplayName != Environment.MachineName
            ? $"👤 {_settings.DisplayName} ({_settings.DeskId ?? "DESK"})"
            : "👤 Login";
        UserButton.Content = label;
    }

    private async void OnUserLoginClicked(object sender, RoutedEventArgs e)
    {
        var loginDlg = new ParticipantLoginDialog(_settings) { Owner = this };
        if (loginDlg.ShowDialog() == true)
        {
            _settings.DisplayName = loginDlg.ParticipantName;
            _settings.DeskId = loginDlg.DeskId;
            _settings.Password = loginDlg.Password;
            _settings.ServerUrl = loginDlg.ServerUrl;
            _config.ServerUrl = loginDlg.ServerUrl;
            _config.DeskId = loginDlg.DeskId;
            _api = new ApiClient(_config.ServerUrl);
            UpdateUserButton();
            await ConnectToControlPlaneAsync();
        }
    }

    private async void OnServerConfigClicked(object sender, RoutedEventArgs e)
    {
        var dlg = new ServerConfigDialog(_config.ServerUrl, _config.DeskId) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            _config.ServerUrl = dlg.ServerUrl;
            _config.DeskId = dlg.DeskId;
            _settings.ServerUrl = dlg.ServerUrl;
            _settings.DeskId = dlg.DeskId;
            _api = new ApiClient(_config.ServerUrl);
            await ConnectToControlPlaneAsync();
        }
    }

    private async void OnTestLocally(object sender, RoutedEventArgs e)
    {
        if (TestLocallyButton.IsEnabled == false) return;
        TestLocallyButton.IsEnabled = false;
        TestLocallyButton.Content = "⏳ Testing...";
        StatusText.Text = "Executing local test suite (node --test)...";
        StatusText.Foreground = (System.Windows.Media.SolidColorBrush)FindResource("Ink");

        try
        {
            var bundledNode = Path.Combine(AppContext.BaseDirectory, "nodejs", "node.exe");
            var nodeExe = File.Exists(bundledNode) ? bundledNode : "node";

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = nodeExe,
                Arguments = "--test test/site.test.js",
                WorkingDirectory = _settings.WorkspacePath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = new System.Diagnostics.Process { StartInfo = psi };
            proc.Start();

            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();

            await proc.WaitForExitAsync();
            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            var output = (stdout + "\n" + stderr).Trim();
            var passed = proc.ExitCode == 0;

            if (passed)
            {
                StatusText.Text = "✔ Local tests passed! Workspace verified and ready to submit.";
                StatusText.Foreground = (System.Windows.Media.SolidColorBrush)FindResource("Ok");
            }
            else
            {
                StatusText.Text = "✖ Local tests failed! Fix the issues before submitting to avoid point penalties.";
                StatusText.Foreground = (System.Windows.Media.SolidColorBrush)FindResource("Alert");
            }

            var dialog = new TestResultsWindow(passed, output, _settings.WorkspacePath)
            {
                Owner = this
            };
            dialog.ShowDialog();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error running local tests: {ex.Message}";
            StatusText.Foreground = (System.Windows.Media.SolidColorBrush)FindResource("Alert");
            MessageBox.Show($"Failed to execute test suite: {ex.Message}", "Test Runner Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            TestLocallyButton.IsEnabled = true;
            TestLocallyButton.Content = "🧪 Test Locally (F6)";
        }
    }

    private void OnTabChanged(object sender, RoutedEventArgs e)
    {
        if (AgyTerm is null || OpenCodeTerm is null) return;

        if (TabAgy.IsChecked == true)
        {
            AgyTerm.Visibility = Visibility.Visible;
            OpenCodeTerm.Visibility = Visibility.Collapsed;
        }
        else
        {
            AgyTerm.Visibility = Visibility.Collapsed;
            OpenCodeTerm.Visibility = Visibility.Visible;
        }
    }

    private void RefreshMeters()
    {
        AgyMeter.Text = _budget.AgyExhausted ? "agy: EXHAUSTED" : "agy: READY";
        AgyMeter.Foreground = _budget.AgyExhausted
            ? (System.Windows.Media.SolidColorBrush)FindResource("Alert")
            : (System.Windows.Media.SolidColorBrush)FindResource("Ok");

        OcMeter.Text = $"OpenCode: {_budget.OpenCodePrompts} / {_budget.OpenCodeCap} prompts";
    }

    private System.Diagnostics.Process? _localSiteProc;

    private void EnsureWorkspaceSeeded()
    {
        try
        {
            if (!File.Exists(Path.Combine(_settings.WorkspacePath, "server.js")))
            {
                var siteSource = Path.Combine(AppContext.BaseDirectory, "site");
                if (Directory.Exists(siteSource))
                {
                    CopyDirectory(siteSource, _settings.WorkspacePath);
                }
            }
        }
        catch { }
    }

    private static void CopyDirectory(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);
        foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(sourceDir, file);
            var dest = Path.Combine(targetDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, overwrite: true);
        }
    }

    private void EnsureLocalSiteRunning()
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(400) };
            var res = client.GetAsync(_settings.SiteUrl).GetAwaiter().GetResult();
            if (res.IsSuccessStatusCode) return;
        }
        catch { }

        try
        {
            var serverJs = Path.Combine(_settings.WorkspacePath, "server.js");
            if (File.Exists(serverJs))
            {
                var bundledNode = Path.Combine(AppContext.BaseDirectory, "nodejs", "node.exe");
                var nodeExe = File.Exists(bundledNode) ? bundledNode : "node";

                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = nodeExe,
                    Arguments = "--watch server.js",
                    WorkingDirectory = _settings.WorkspacePath,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                _localSiteProc = System.Diagnostics.Process.Start(psi);
            }
        }
        catch { }
    }

    private void ReloadSiteSafe()
    {
        if (Site.CoreWebView2 != null)
        {
            Site.CoreWebView2.Navigate(_settings.SiteUrl);
        }
        else
        {
            Site.Source = new Uri(_settings.SiteUrl);
        }
    }

    private void OnReloadSite(object sender, RoutedEventArgs e) => ReloadSiteSafe();

    private void OnLayoutAll3(object sender, RoutedEventArgs e)
    {
        ColWeb.Width = new GridLength(1.1, GridUnitType.Star);
        ColCode.Width = new GridLength(1.2, GridUnitType.Star);
        ColTerm.Width = new GridLength(1.2, GridUnitType.Star);
        ColSplitter1.Width = new GridLength(6);
        ColSplitter2.Width = new GridLength(6);
    }

    private void OnLayoutWebTerm(object sender, RoutedEventArgs e)
    {
        ColWeb.Width = new GridLength(1, GridUnitType.Star);
        ColCode.Width = new GridLength(0);
        ColTerm.Width = new GridLength(1, GridUnitType.Star);
        ColSplitter1.Width = new GridLength(0);
        ColSplitter2.Width = new GridLength(6);
    }

    private void OnLayoutCodeTerm(object sender, RoutedEventArgs e)
    {
        ColWeb.Width = new GridLength(0);
        ColCode.Width = new GridLength(1.1, GridUnitType.Star);
        ColTerm.Width = new GridLength(1.2, GridUnitType.Star);
        ColSplitter1.Width = new GridLength(0);
        ColSplitter2.Width = new GridLength(6);
    }
}
