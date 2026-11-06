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
    private int _reloading;

    public event Action? ReloadRequested;
    public event Action<string>? Problem;

    public SiteHost(string nodePath, string workspace, string siteUrl, string dataPath)
    {
        _node = nodePath; _dir = workspace; _siteUrl = siteUrl.TrimEnd('/');
        Directory.CreateDirectory(dataPath);
        _dbPath = Path.Combine(dataPath, "site.db");   // outside the workspace so it is never uploaded
    }

    public void Start()
    {
        var fallbackNode = Path.Combine(AppContext.BaseDirectory, "node", "node.exe");
        var node = ExeResolver.Resolve(_node)
            ?? (File.Exists(fallbackNode) ? fallbackNode : null)
            ?? throw new FileNotFoundException("Node.js was not found. Set NodePath in kiosk.json.");
        var psi = new ProcessStartInfo(node)
        {
            WorkingDirectory = _dir, UseShellExecute = false, CreateNoWindow = true,
        };
        foreach (var a in new[] { "--watch", "--no-warnings", "server.js" }) psi.ArgumentList.Add(a);
        psi.Environment["PORT"] = new Uri(_siteUrl).Port.ToString();
        psi.Environment["HOST"] = "127.0.0.1";
        psi.Environment["DB_FILE"] = _dbPath;
        _proc = Process.Start(psi);

        _watcher = new FileSystemWatcher(_dir) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size };
        FileSystemEventHandler h = (_, e) => OnFileEvent(e.FullPath);
        _watcher.Changed += h; _watcher.Created += h; _watcher.Deleted += h;
        _watcher.Renamed += (_, e) => OnFileEvent(e.FullPath);
        _watcher.EnableRaisingEvents = true;
    }

    private static bool Ignored(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}") || path.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}")
        || path.Contains(".breach-backup") || path.EndsWith(".db") || path.EndsWith(".db-wal") || path.EndsWith(".db-shm");

    /// <summary>Waits for the first successful answer from the site (used before the first page load).</summary>
    public async Task<bool> WaitHealthyAsync(int seconds)
    {
        for (var i = 0; i < seconds * 4; i++)
        {
            try { using var res = await Http.GetAsync(_siteUrl + "/api/health"); if (res.IsSuccessStatusCode) return true; }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) { }
            await Task.Delay(250);
        }
        return false;
    }

    private void OnFileEvent(string path)
    {
        if (Ignored(path)) return;
        // Many editors write several times per save: wait for things to settle, then reload.
        _debounce?.Dispose();
        _debounce = new System.Threading.Timer(_ => _ = ReloadWhenHealthyAsync(), null, 700, Timeout.Infinite);
    }

    /// <summary>Ask for a reload after the site has restarted (used after an attack lands and after a verdict).</summary>
    public void RequestReload() => _ = ReloadWhenHealthyAsync();

    private async Task ReloadWhenHealthyAsync()
    {
        if (Interlocked.Exchange(ref _reloading, 1) == 1) return;
        try
        {
            await Task.Delay(600); // give `node --watch` time to restart the process
            for (var i = 0; i < 30; i++)
            {
                try
                {
                    using var res = await Http.GetAsync(_siteUrl + "/api/health");
                    if (res.IsSuccessStatusCode) { ReloadRequested?.Invoke(); return; }
                }
                catch (HttpRequestException) { }
                catch (TaskCanceledException) { }
                await Task.Delay(300);
            }
            Problem?.Invoke("The website is not running. Check your last change.");
        }
        finally { Interlocked.Exchange(ref _reloading, 0); }
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _debounce?.Dispose();
        try { _proc?.Kill(entireProcessTree: true); } catch { }
    }
}
