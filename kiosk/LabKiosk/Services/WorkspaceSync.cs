using System.IO;
using System.Security.Cryptography;

namespace LabKiosk.Services;

/// <summary>
/// Pulls the server's copy of the workspace (used when an attack lands and after a restart). Every replaced file is first
/// copied to .breach-backup/&lt;tag&gt;/ so the participant's work is never lost.
/// </summary>
public sealed class WorkspaceSync(ApiClient api, string participantId, string root, SnapshotSync snapshots)
{
    private static readonly string[] Blocked = { "node_modules", ".git", ".breach-backup" };

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
                var backup = Path.Combine(root, ".breach-backup", tag, f.Path.Replace('/', Path.DirectorySeparatorChar));
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
