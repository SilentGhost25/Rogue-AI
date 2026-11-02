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

    public WorkspaceStore(IConfiguration config)
    {
        _root = Path.GetFullPath(config["Breach:WorkspacesPath"] ?? "data/workspaces");
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

    public (int written, List<string> rejected) Save(string participantId, IEnumerable<SnapshotFile> files)
    {
        var written = 0;
        var rejected = new List<string>();
        Directory.CreateDirectory(PathFor(participantId));
        foreach (var f in files)
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
}

public sealed record SnapshotFile(string Path, string Sha256, string ContentBase64);
public sealed record SnapshotRequest(List<SnapshotFile> Files);
