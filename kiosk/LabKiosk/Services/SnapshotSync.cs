using System.Security.Cryptography;

namespace LabKiosk.Services;

/// <summary>
/// Uploads changed files (by content hash) every few seconds. Excludes node_modules, build output and backups.
/// </summary>
public sealed class SnapshotSync : IDisposable
{
    private static readonly string[] ExcludedExtensions = { ".db", ".db-wal", ".db-shm", ".sqlite", ".log" };
    private static readonly string[] ExcludedDirs = { "node_modules", ".git", ".breach-backup", "dist", "build", "coverage" };
    private const long MaxFileBytes = 2 * 1024 * 1024;

    private readonly ApiClient _api;
    private readonly string _participantId;
    private readonly string _root;
    private readonly Dictionary<string, string> _lastHashes = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Timer? _timer;

    public SnapshotSync(ApiClient api, string participantId, string root)
    {
        _api = api; _participantId = participantId; _root = root;
    }

    public void Start(TimeSpan interval)
    {
        _timer = new Timer(async _ => await TryFlushAsync(), null, TimeSpan.Zero, interval);
    }

    private async Task TryFlushAsync() { try { await FlushAsync(); } catch { /* retried on the next tick */ } }

    public async Task FlushAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var changed = new List<SnapshotFileDto>();
            var pending = new Dictionary<string, string>();
            foreach (var file in Walk(_root))
            {
                var bytes = await TryReadFileBytesAsync(file);
                if (bytes == null) continue;

                var hash = Convert.ToHexString(SHA256.HashData(bytes));
                var rel = Path.GetRelativePath(_root, file).Replace('\\', '/');
                if (_lastHashes.TryGetValue(rel, out var prev) && prev == hash) continue;
                changed.Add(new SnapshotFileDto(rel, hash, Convert.ToBase64String(bytes)));
                pending[rel] = hash;
            }
            if (changed.Count == 0) return;
            await _api.UploadSnapshotAsync(_participantId, changed);
            foreach (var (k, v) in pending) _lastHashes[k] = v; // only remember hashes once the upload succeeded
        }
        finally { _gate.Release(); }
    }

    private static async Task<byte[]?> TryReadFileBytesAsync(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var ms = new MemoryStream();
            await fs.CopyToAsync(ms);
            return ms.ToArray();
        }
        catch
        {
            return null;
        }
    }

    private IEnumerable<string> Walk(string dir)
    {
        if (!Directory.Exists(dir)) yield break;
        foreach (var f in Directory.EnumerateFiles(dir))
        {
            var ext = Path.GetExtension(f);
            if (ExcludedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase)) continue;
            if (new FileInfo(f).Length <= MaxFileBytes) yield return f;
        }
        foreach (var d in Directory.EnumerateDirectories(dir))
            if (!ExcludedDirs.Contains(Path.GetFileName(d))) foreach (var f in Walk(d)) yield return f;
    }

    public void Dispose() { _timer?.Dispose(); _gate.Dispose(); }
}
