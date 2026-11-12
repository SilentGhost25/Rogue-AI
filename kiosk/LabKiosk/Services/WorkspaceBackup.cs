using System.IO;
using System.IO.Compression;

namespace LabKiosk.Services;

/// <summary>Copies the participant's source files to .breach-backup/&lt;tag&gt; and manages strict clean-slate resets.</summary>
public static class WorkspaceBackup
{
    private static readonly string[] Skip = { "node_modules", ".git", ".breach-backup" };

    public static void Snapshot(string root, string tag)
    {
        try { Copy(root, Path.Combine(root, ".breach-backup", tag)); } catch { /* a failed backup must never block the event */ }
    }

    public static void CopyCleanTemplate(string src, string dst) => Copy(src, dst);

    /// <summary>
    /// Ensures the participant workspace is populated with a pristine baseline if missing or empty.
    /// Extracts site.zip if bundled next to the executable, otherwise copies the clean site template directory.
    /// </summary>
    public static void EnsurePristineWorkspace(string workspacePath, string? siteTemplatePath)
    {
        try
        {
            Directory.CreateDirectory(workspacePath);
            var isEmptyOrIncomplete = !Directory.EnumerateFileSystemEntries(workspacePath).Any()
                                   || !File.Exists(Path.Combine(workspacePath, "server.js"));
            if (!isEmptyOrIncomplete) return;

            var zipCandidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "site.zip"),
                Path.Combine(AppContext.BaseDirectory, "site-template.zip")
            };
            var zipPath = zipCandidates.FirstOrDefault(File.Exists);
            if (zipPath is not null)
            {
                ZipFile.ExtractToDirectory(zipPath, workspacePath, overwriteFiles: true);
                return;
            }

            if (!string.IsNullOrWhiteSpace(siteTemplatePath) && Directory.Exists(siteTemplatePath))
            {
                CopyCleanTemplate(siteTemplatePath, workspacePath);
            }
        }
        catch { /* MainWindow.StartAsync still verifies & pulls baseline from Control Plane if needed */ }
    }

    /// <summary>
    /// Strict workspace reset on close: removes all files and folders inside the workspace.
    /// Individual failures on locked handles are caught gracefully so cleanup never crashes shutdown.
    /// </summary>
    public static void WipeWorkspaceContents(string workspacePath)
    {
        if (string.IsNullOrWhiteSpace(workspacePath) || !Directory.Exists(workspacePath)) return;
        try
        {
            foreach (var file in Directory.EnumerateFiles(workspacePath))
            {
                try
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                    File.Delete(file);
                }
                catch { /* best effort for locked files */ }
            }
            foreach (var dir in Directory.EnumerateDirectories(workspacePath))
            {
                try
                {
                    Directory.Delete(dir, recursive: true);
                }
                catch
                {
                    WipeWorkspaceContents(dir);
                    try { Directory.Delete(dir, recursive: false); } catch { }
                }
            }
        }
        catch { /* graceful shutdown */ }
    }

    /// <summary>Clears local SQLite database files so reopening starts with fresh participant site data.</summary>
    public static void WipeSiteDb(string dataPath)
    {
        if (string.IsNullOrWhiteSpace(dataPath) || !Directory.Exists(dataPath)) return;
        try
        {
            foreach (var f in Directory.EnumerateFiles(dataPath, "site.db*"))
            {
                try { File.Delete(f); } catch { }
            }
        }
        catch { }
    }

    private static void Copy(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.EnumerateFiles(src))
        {
            var name = Path.GetFileName(f);
            if (name.EndsWith(".db") || name.EndsWith(".db-wal") || name.EndsWith(".db-shm")) continue;
            File.Copy(f, Path.Combine(dst, name), overwrite: true);
        }
        foreach (var d in Directory.EnumerateDirectories(src))
            if (!Skip.Contains(Path.GetFileName(d))) Copy(d, Path.Combine(dst, Path.GetFileName(d)));
    }

    /// <summary>Resolve a writable workspace directory with multi-tier resilient fallback.</summary>
    public static string ResolveWorkspace(string preferred)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(preferred)) candidates.Add(preferred);
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "BreachWorkspace"));
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BreachKiosk", "workspace"));
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "workspace"));

        foreach (var c in candidates)
        {
            try
            {
                Directory.CreateDirectory(c);
                var probe = Path.Combine(c, ".write-test");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                return c;
            }
            catch { }
        }

        return preferred;
    }
}
