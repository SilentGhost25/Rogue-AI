using System.IO;
using System.Security.Cryptography;

namespace LabKiosk.Services;

/// <summary>
/// Pulls the server's copy of the workspace (used when an attack lands and after a restart). Every replaced file is first
/// copied to .breach-backup/&lt;tag&gt;/ so the participant's work is never lost.
/// </summary>
public sealed class WorkspaceSync(ApiClient api, string participantId, string root, SnapshotSync snapshots)
{
    private static readonly string[] Blocked = { "node_modules", ".git", ".breach-backup", "dist", "build", "coverage" };

    private static bool IsIgnoredRuntimeFile(string rel)
    {
        var name = Path.GetFileName(rel);
        if (rel.Split('/').Any(p => Blocked.Contains(p))) return true;
        if (name.EndsWith(".db", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".db-wal", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".db-shm", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".sqlite", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".sqlite3", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".log", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("eval_", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith(".write-test", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    // ---- Touch-Penalty baseline -----------------------------------------------------------------------
    // Populated after each attack patch lands. Maps relative path → SHA-256 hex of the file at that moment.
    // Files in _attackPatchedFiles are the allowed-to-change set for the current active attack.
    private Dictionary<string, string> _baselineHashes = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _attackPatchedFiles = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Takes a full baseline snapshot of the workspace and records which files the attack payload touched.
    /// Call this immediately after ApplyAsync returns for an incoming attack.
    /// </summary>
    public void RecordBaseline(IEnumerable<string> patchedRelPaths)
    {
        var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (IsIgnoredRuntimeFile(rel)) continue;
            try
            {
                var bytes = File.ReadAllBytes(file);
                hashes[rel] = Convert.ToHexString(SHA256.HashData(bytes));
            }
            catch { /* locked/transient — skip */ }
        }
        _baselineHashes = hashes;
        _attackPatchedFiles = new HashSet<string>(patchedRelPaths, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns relative paths of files modified outside the current attack's scope.
    /// An empty list means the participant played fair.
    /// </summary>
    public List<string> CheckOutOfScopeEdits()
    {
        if (_baselineHashes.Count == 0) return new(); // No baseline recorded — no active attack yet.
        var violations = new List<string>();

        // Check existing files for mutations outside attack scope
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (IsIgnoredRuntimeFile(rel)) continue;
            if (_attackPatchedFiles.Contains(rel)) continue; // allowed to change

            if (_baselineHashes.TryGetValue(rel, out var originalHash))
            {
                try
                {
                    var current = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
                    if (!current.Equals(originalHash, StringComparison.OrdinalIgnoreCase))
                        violations.Add(rel);
                }
                catch { }
            }
            else
            {
                violations.Add(rel); // New file added outside attack scope
            }
        }

        // Check for deleted files outside attack scope
        foreach (var rel in _baselineHashes.Keys)
        {
            if (IsIgnoredRuntimeFile(rel)) continue;
            if (_attackPatchedFiles.Contains(rel)) continue;
            var full = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(full))
                violations.Add(rel + " [deleted]");
        }

        return violations;
    }

    /// <summary>Clears the baseline (call when attack is solved or new attack lands).</summary>
    public void ClearBaseline()
    {
        _baselineHashes.Clear();
        _attackPatchedFiles.Clear();
    }

    public async Task<int> PullAsync(string tag)
    {
        var remote = await api.ListFilesAsync(participantId);
        var differing = new System.Collections.Concurrent.ConcurrentBag<string>();

        await Parallel.ForEachAsync(remote, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, async (r, _) =>
        {
            var local = SafeLocal(r.Path);
            if (local is null) return;
            if (!File.Exists(local))
            {
                differing.Add(r.Path);
                return;
            }
            try
            {
                var bytes = await File.ReadAllBytesAsync(local);
                var hash = Convert.ToHexString(SHA256.HashData(bytes));
                if (!hash.Equals(r.Sha256, StringComparison.OrdinalIgnoreCase))
                    differing.Add(r.Path);
            }
            catch
            {
                differing.Add(r.Path);
            }
        });

        var written = 0;
        foreach (var batch in differing.Chunk(40))
            written += await ApplyAsync(tag, await api.FetchFilesAsync(participantId, batch));
        return written;
    }

    /// <summary>Writes server files into the workspace, verifying each hash and backing up whatever it replaces.</summary>
    public async Task<int> ApplyAsync(string tag, IEnumerable<SnapshotFileDto> files)
    {
        var written = 0;
        foreach (var f in files)
        {
            var bytes = Convert.FromBase64String(f.ContentBase64);
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            var target = SafeLocal(f.Path);
            if (target is null || !hash.Equals(f.Sha256, StringComparison.OrdinalIgnoreCase)) continue;
            if (File.Exists(target) && Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(target))) == hash)
            {
                snapshots.MarkSynced(f.Path, hash);   // already identical
                continue;
            }
            if (File.Exists(target))
            {
                var backupRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BreachKiosk", "backups");
                var backup = Path.Combine(backupRoot, tag, f.Path.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                File.Copy(target, backup, overwrite: true);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await File.WriteAllBytesAsync(target, bytes);
            snapshots.MarkSynced(f.Path, hash);   // so the next upload does not echo it back
            written++;
        }
        return written;
    }

    private string? SafeLocal(string rel)
    {
        if (string.IsNullOrWhiteSpace(rel) || Path.IsPathRooted(rel)) return null;
        var parts = rel.Replace('\\', '/').Split('/');
        if (parts.Any(p => p is ".." or "." or "" || Blocked.Contains(p))) return null;
        var baseDir = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(baseDir, rel));
        return full.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase) ? full : null;
    }
}
