using System.IO;
using System.Security.Cryptography;

namespace LabKiosk.Services;

/// <summary>
/// Uploads changed files (by content hash) and deletions every few seconds. While an attack is landing the sync is held, so an
/// older local file can never overwrite the attack's files on the server.
/// </summary>
public sealed class SnapshotSync : IDisposable
{
    private static readonly string[] ExcludedDirs = { "node_modules", ".git", ".breach-backup", "dist", "build", "coverage" };
    private const long MaxFileBytes = 2 * 1024 * 1024;

    private readonly ApiClient _api;
    private readonly string _participantId, _root;
    private readonly Dictionary<string, string> _known = new();   // path -> hash the server is known to have
    private readonly Dictionary<string, (long Length, DateTime LastWrite)> _statCache = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Timer? _timer;
    private volatile bool _held;

    public SnapshotSync(ApiClient api, string participantId, string root) { _api = api; _participantId = participantId; _root = root; }

    public bool Held => _held;
    public void Hold() => _held = true;
    public void Release() => _held = false;
    public void MarkSynced(string rel, string hash)
    {
        lock (_known)
        {
            _known[rel] = hash;
            var path = Path.Combine(_root, rel);
            if (File.Exists(path))
            {
                var fi = new FileInfo(path);
                _statCache[rel] = (fi.Length, fi.LastWriteTimeUtc);
            }
        }
    }

    public void Start(TimeSpan interval) => _timer = new Timer(async _ => await TryFlushAsync(), null, interval, interval);

    private async Task TryFlushAsync() { try { await FlushAsync(); } catch { /* retried on the next tick */ } }

    /// <param name="force">Flush even while held (used for the pre-attack flush, which happens just before the hold).</param>
    public async Task FlushAsync(bool force = false)
    {
        if (_held && !force) return;
        await _gate.WaitAsync();
        try
        {
            var changed = new List<SnapshotFileDto>();
            var pending = new Dictionary<string, string>();
            var present = new HashSet<string>();
            foreach (var file in Walk(_root))
            {
                var rel = Path.GetRelativePath(_root, file).Replace('\\', '/');
                present.Add(rel);

                var fi = new FileInfo(file);
                lock (_known)
                {
                    if (_statCache.TryGetValue(rel, out var stat) &&
                        stat.Length == fi.Length &&
                        stat.LastWrite == fi.LastWriteTimeUtc &&
                        _known.ContainsKey(rel))
                    {
                        continue; // File completely unchanged since last hash
                    }
                }

                byte[] bytes;
                try { bytes = await File.ReadAllBytesAsync(file); } catch (IOException) { continue; } // editor still writing; next tick
                var hash = Convert.ToHexString(SHA256.HashData(bytes));
                bool same; lock (_known) same = _known.TryGetValue(rel, out var prev) && prev == hash;

                lock (_known) { _statCache[rel] = (fi.Length, fi.LastWriteTimeUtc); }

                if (same) continue;
                changed.Add(new SnapshotFileDto(rel, hash, Convert.ToBase64String(bytes)));
                pending[rel] = hash;
            }
            List<string> deleted; lock (_known) deleted = _known.Keys.Where(k => !present.Contains(k)).ToList();
            if (changed.Count == 0 && deleted.Count == 0) return;

            await _api.UploadSnapshotAsync(_participantId, changed, deleted);
            lock (_known)
            {
                foreach (var (k, v) in pending) _known[k] = v;   // remember only after a successful upload
                foreach (var d in deleted)
                {
                    _known.Remove(d);
                    _statCache.Remove(d);
                }
            }
        }
        finally { _gate.Release(); }
    }

    private static bool IsLocalOnly(string name) =>
        name.EndsWith(".db", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".db-wal", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".db-shm", StringComparison.OrdinalIgnoreCase) || name == ".gitignore";

    private IEnumerable<string> Walk(string dir)
    {
        if (!Directory.Exists(dir)) yield break;
        foreach (var f in Directory.EnumerateFiles(dir))
            if (!IsLocalOnly(Path.GetFileName(f)) && new FileInfo(f).Length <= MaxFileBytes) yield return f;
        foreach (var d in Directory.EnumerateDirectories(dir))
            if (!ExcludedDirs.Contains(Path.GetFileName(d))) foreach (var f in Walk(d)) yield return f;
    }

    public void Dispose() { _timer?.Dispose(); _gate.Dispose(); }
}
