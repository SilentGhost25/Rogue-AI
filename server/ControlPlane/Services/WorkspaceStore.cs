namespace ControlPlane.Services;

/// <summary>
/// Stores uploaded snapshot files in a per-participant folder on the server.
/// SECURITY-CRITICAL (human review required): every path from a kiosk is untrusted.
/// </summary>
public sealed class WorkspaceStore
{
    private static readonly string[] BlockedSegments = { "node_modules", ".git", ".breach-backup" };
    private const long MaxFileBytes = 2 * 1024 * 1024;
    private readonly string _root;
    private readonly string _template;

    public WorkspaceStore(IConfiguration config)
    {
        _root = Path.GetFullPath(config["Breach:WorkspacesPath"] ?? "data/workspaces");
        var configuredTemplate = config["Breach:SiteTemplatePath"];
        if (!string.IsNullOrEmpty(configuredTemplate) && Directory.Exists(configuredTemplate))
        {
            _template = Path.GetFullPath(configuredTemplate);
        }
        else if (Directory.Exists("site"))
        {
            _template = Path.GetFullPath("site");
        }
        else
        {
            _template = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "site"));
        }
        Directory.CreateDirectory(_root);
    }

    public string PathFor(string participantId) => Path.Combine(_root, SafeId(participantId));

    private static string SafeId(string id)
    {
        if (id.Length == 0 || id.Length > 64 || !id.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '-' || ch == '_'))
            throw new ArgumentException("Invalid participant id");
        return id;
    }

    /// <summary>Returns null if the path escapes the workspace or hits a blocked segment.</summary>
    public string? ResolveSafe(string participantId, string relativePath)
    {
        var baseDir = Path.GetFullPath(PathFor(participantId)) + Path.DirectorySeparatorChar;
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath)) return null;
        var segments = relativePath.Replace('\\', '/').Split('/');
        if (segments.Any(s => s == ".." || s == "." || s.Length == 0 || BlockedSegments.Contains(s))) return null;
        var full = Path.GetFullPath(Path.Combine(baseDir, relativePath));
        return full.StartsWith(baseDir, StringComparison.Ordinal) ? full : null;
    }

    public IReadOnlyList<FileEntry> List(string participantId)
    {
        var dir = PathFor(participantId);
        var result = new List<FileEntry>();
        if (!Directory.Exists(dir)) return result;
        foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(dir, file).Replace('\\', '/');
            if (ResolveSafe(participantId, rel) is null) continue;
            if (new FileInfo(file).Length > MaxFileBytes) continue;
            result.Add(new FileEntry(rel, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file)))));
        }
        return result;
    }

    public List<SnapshotFile> Read(string participantId, IEnumerable<string> paths)
    {
        var files = new List<SnapshotFile>();
        foreach (var rel in paths.Take(500))
        {
            var full = ResolveSafe(participantId, rel);
            if (full is null || !File.Exists(full)) continue;
            var bytes = File.ReadAllBytes(full);
            files.Add(new SnapshotFile(rel, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)), Convert.ToBase64String(bytes)));
        }
        return files;
    }

    public (int written, List<string> rejected) Save(string participantId, IEnumerable<SnapshotFile> files, IEnumerable<string>? deleted = null)
    {
        foreach (var rel in (deleted ?? Array.Empty<string>()).Take(500))
        {
            var gone = ResolveSafe(participantId, rel);
            if (gone is not null && File.Exists(gone)) File.Delete(gone);
        }
        var written = 0;
        var rejected = new List<string>();
        Directory.CreateDirectory(PathFor(participantId));
        foreach (var f in files.Take(500))
        {
            var target = ResolveSafe(participantId, f.Path);
            byte[] bytes;
            try { bytes = Convert.FromBase64String(f.ContentBase64); } catch (FormatException) { rejected.Add(f.Path); continue; }
            if (target is null || bytes.Length > MaxFileBytes
                || !Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).Equals(f.Sha256, StringComparison.OrdinalIgnoreCase))
            { rejected.Add(f.Path); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllBytes(target, bytes);
            written++;
        }
        return (written, rejected);
    }

    /// <summary>Restores specific relative paths in a participant workspace back to the clean site template.</summary>
    public bool RestoreFiles(string participantId, IEnumerable<string> relPaths)
    {
        var dir = PathFor(participantId);
        if (!Directory.Exists(dir)) return false;
        foreach (var rel in relPaths)
        {
            var src = Path.Combine(_template, rel.Replace('/', Path.DirectorySeparatorChar));
            var dst = ResolveSafe(participantId, rel);
            if (dst is not null && File.Exists(src))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                File.Copy(src, dst, overwrite: true);
            }
        }
        return true;
    }
}

public sealed record FileEntry(string Path, string Sha256);
public sealed record SnapshotFile(string Path, string Sha256, string ContentBase64);
public sealed record SnapshotRequest(List<SnapshotFile> Files, List<string>? Deleted = null);
public sealed record FetchRequest(List<string> Paths);
