using System.Diagnostics;
using System.IO;
using System.Net.Http;

namespace LabKiosk.Services;

/// <summary>
/// Runs the participant's website from their workspace with `node --watch`, so an edit by agy or OpenCode restarts the site,
/// and tells the window to reload the page once the site answers again. This is the "live on the website" path.
/// </summary>
public sealed class SiteHost : IDisposable
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(2) };
    private readonly string _node, _dir, _siteUrl, _dbPath;
    private Process? _proc;
    private FileSystemWatcher? _watcher;
    private System.Threading.Timer? _debounce;
    private System.Threading.Timer? _healthSentinel;
    private int _reloading;
    private int _reloadPending;
    private int _sentinelRunning;
    private bool _isCurrentlyHealthy;
    private readonly System.Text.StringBuilder _stderrLog = new();
    private readonly object _stderrLock = new();

    public event Action? ReloadRequested;
    public event Action? SiteRestored;
    public event Action<string?>? SiteDown;
    public event Action<string>? Problem;

    public string? LastError
    {
        get
        {
            lock (_stderrLock) { return _stderrLog.Length > 0 ? _stderrLog.ToString() : null; }
        }
    }

    public bool IsProcessAlive => _proc is { HasExited: false };

    public SiteHost(string nodePath, string workspace, string siteUrl, string dataPath)
    {
        _node = nodePath; _dir = workspace; _siteUrl = siteUrl.TrimEnd('/');
        Directory.CreateDirectory(dataPath);
        _dbPath = Path.Combine(dataPath, "site.db");   // outside the workspace so it is never uploaded
    }

    public void Start()
    {
        StartProcessInternal();

        _watcher = new FileSystemWatcher(_dir) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size };
        FileSystemEventHandler h = (_, e) => OnFileEvent(e.FullPath);
        _watcher.Changed += h; _watcher.Created += h; _watcher.Deleted += h;
        _watcher.Renamed += (_, e) => OnFileEvent(e.FullPath);
        _watcher.EnableRaisingEvents = true;

        // Background Health Sentinel: polls every 1000ms to auto-detect when Node boots/recovers
        _healthSentinel = new System.Threading.Timer(_ => _ = CheckHealthSentinelAsync(), null, 1000, 1000);
    }

    private void StartProcessInternal()
    {
        try
        {
            if (_proc is not null && !_proc.HasExited)
            {
                return;
            }
        }
        catch { }

        var fallbackNode = Path.Combine(AppContext.BaseDirectory, "node", "node.exe");
        var fallbackNodeJs = Path.Combine(AppContext.BaseDirectory, "nodejs", "node.exe");
        var node = ExeResolver.Resolve(_node)
            ?? (File.Exists(fallbackNode) ? fallbackNode : null)
            ?? (File.Exists(fallbackNodeJs) ? fallbackNodeJs : null)
            ?? throw new FileNotFoundException("Node.js was not found. Set NodePath in kiosk.json.");
        var psi = new ProcessStartInfo(node)
        {
            WorkingDirectory = _dir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
        };
        foreach (var a in new[] { "--watch", "--no-warnings", "server.js" }) psi.ArgumentList.Add(a);
        if (Uri.TryCreate(_siteUrl, UriKind.Absolute, out var siteUri))
            psi.Environment["PORT"] = siteUri.Port.ToString();
        else
            psi.Environment["PORT"] = "3000";
        psi.Environment["HOST"] = "127.0.0.1";
        psi.Environment["DB_FILE"] = _dbPath;

        var nodeModules = ResolveNodeModules();
        if (nodeModules is not null)
        {
            var existing = Environment.GetEnvironmentVariable("NODE_PATH");
            psi.Environment["NODE_PATH"] = string.IsNullOrWhiteSpace(existing)
                ? nodeModules
                : $"{nodeModules}{Path.PathSeparator}{existing}";
        }

        try
        {
            _proc = Process.Start(psi);
            if (_proc is not null)
            {
                _proc.ErrorDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        lock (_stderrLock)
                        {
                            _stderrLog.AppendLine(e.Data);
                            if (_stderrLog.Length > 16384)
                            {
                                _stderrLog.Remove(0, 8192);
                            }
                        }
                    }
                };
                _proc.BeginErrorReadLine();
            }
        }
        catch (Exception ex)
        {
            lock (_stderrLock)
            {
                _stderrLog.AppendLine($"Failed to start Node process: {ex.Message}");
            }
        }
    }

    private async Task CheckHealthSentinelAsync()
    {
        if (Interlocked.Exchange(ref _sentinelRunning, 1) == 1) return;
        try
        {
            if (_proc is null || _proc.HasExited)
            {
                StartProcessInternal();
            }

            bool healthy = false;
            try
            {
                using var res = await Http.GetAsync(_siteUrl + "/api/health");
                if (res.IsSuccessStatusCode || (int)res.StatusCode == 503)
                {
                    healthy = true;
                }
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) { }

            if (healthy && !_isCurrentlyHealthy)
            {
                _isCurrentlyHealthy = true;
                lock (_stderrLock) { _stderrLog.Clear(); }
                SiteRestored?.Invoke();
                ReloadRequested?.Invoke();
            }
            else if (!healthy)
            {
                _isCurrentlyHealthy = false;
            }
        }
        catch { }
        finally
        {
            Interlocked.Exchange(ref _sentinelRunning, 0);
        }
    }

    private string? ResolveNodeModules()
    {
        var candidates = new List<string>
        {
            Path.Combine(_dir, "node_modules"),
            Path.Combine(AppContext.BaseDirectory, "node_modules"),
            Path.Combine(AppContext.BaseDirectory, "site", "node_modules"),
            Path.Combine(AppContext.BaseDirectory, "site-template", "node_modules"),
            Path.Combine(Path.GetPathRoot(AppContext.BaseDirectory) ?? "C:\\", "BreachWorkspace", "node_modules"),
            @"C:\BreachWorkspace\node_modules",
            @"D:\BreachWorkspace\node_modules",
        };
        var probe = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 6 && probe is not null; i++, probe = probe.Parent)
            candidates.Add(Path.Combine(probe.FullName, "site", "node_modules"));

        return candidates.FirstOrDefault(d => Directory.Exists(Path.Combine(d, "express")));
    }

    private static bool Ignored(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}") || path.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}")
        || path.Contains(".breach-backup") || path.EndsWith(".db") || path.EndsWith(".db-wal") || path.EndsWith(".db-shm");

    /// <summary>Waits for the first successful answer from the site (used before the first page load).</summary>
    public async Task<bool> WaitHealthyAsync(int seconds)
    {
        for (var i = 0; i < seconds * 15; i++)
        {
            if (_proc is null || _proc.HasExited)
            {
                StartProcessInternal();
            }
            try
            {
                using var res = await Http.GetAsync(_siteUrl + "/api/health");
                if (res.IsSuccessStatusCode || (int)res.StatusCode == 503)
                {
                    _isCurrentlyHealthy = true;
                    return true;
                }
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) { }
            await Task.Delay(65);
        }
        _isCurrentlyHealthy = false;
        return false;
    }

    private void OnFileEvent(string path)
    {
        if (Ignored(path)) return;
        // Many editors write several times per save: wait for things to settle, then reload.
        _debounce?.Dispose();
        _debounce = new System.Threading.Timer(_ => _ = ReloadWhenHealthyAsync(), null, 250, Timeout.Infinite);
    }

    /// <summary>Ask for a reload after the site has restarted (used after an attack lands and after a verdict).</summary>
    public void RequestReload() => _ = ReloadWhenHealthyAsync();

    /// <summary>Waits for `node --watch` to swap workers and answer on the local port.</summary>
    public async Task<bool> WaitRestartHealthyAsync(int settleMs = 550)
    {
        await Task.Delay(settleMs);
        for (var i = 0; i < 40; i++)
        {
            if (_proc is null || _proc.HasExited)
            {
                StartProcessInternal();
            }
            try
            {
                using var res = await Http.GetAsync(_siteUrl + "/api/health");
                // Accept 2xx or 503 (e.g. compromised entrypoint) so WebView2 renders the current state
                if (res.IsSuccessStatusCode || (int)res.StatusCode == 503)
                {
                    _isCurrentlyHealthy = true;
                    return true;
                }
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) { }
            await Task.Delay(100);
        }
        _isCurrentlyHealthy = false;
        return false;
    }

    private async Task ReloadWhenHealthyAsync()
    {
        if (Interlocked.Exchange(ref _reloading, 1) == 1)
        {
            Interlocked.Exchange(ref _reloadPending, 1);
            return;
        }
        try
        {
            do
            {
                Interlocked.Exchange(ref _reloadPending, 0);
                if (await WaitRestartHealthyAsync(500))
                {
                    lock (_stderrLock) { _stderrLog.Clear(); }
                    SiteRestored?.Invoke();
                    ReloadRequested?.Invoke();
                }
                else
                {
                    Problem?.Invoke("The website is not running. Check your last change.");
                    SiteDown?.Invoke(LastError);
                }
            } while (Interlocked.CompareExchange(ref _reloadPending, 0, 0) == 1);
        }
        finally { Interlocked.Exchange(ref _reloading, 0); }
    }

    public void Dispose()
    {
        _healthSentinel?.Dispose();
        _watcher?.Dispose();
        _debounce?.Dispose();
        try
        {
            if (_proc is not null && !_proc.HasExited)
            {
                _proc.Kill(entireProcessTree: true);
                _proc.WaitForExit(1500);
            }
        }
        catch { }
    }
}
