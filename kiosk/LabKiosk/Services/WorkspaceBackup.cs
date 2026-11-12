using System.IO;
using System.IO.Compression;

namespace LabKiosk.Services;

/// <summary>Backs up files to isolated local app data and manages strict clean-slate resets without local Git.</summary>
public static class WorkspaceBackup
{
    private static readonly string[] Skip = { "node_modules", ".git", ".breach-backup" };

    public static void Snapshot(string root, string tag)
    {
        try
        {
            var backupDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BreachKiosk", "backups", tag);
            Copy(root, backupDir);
        }
        catch { /* a failed backup must never block the event */ }
    }

    public static void CopyCleanTemplate(string src, string dst) => Copy(src, dst);

    /// <summary>
    /// Ensures the participant workspace is populated with a pristine baseline if missing or empty.
    /// Extracts site.zip if bundled next to the executable, otherwise copies the clean site template directory.
    /// Also purges any legacy Git or backup folders inside the workspace.
    /// </summary>
    public static void EnsurePristineWorkspace(string workspacePath, string? siteTemplatePath)
    {
        try
        {
            Directory.CreateDirectory(workspacePath);
            WipeLocalGitRepo(workspacePath);
            var legacyBackup = Path.Combine(workspacePath, ".breach-backup");
            if (Directory.Exists(legacyBackup))
            {
                try { Directory.Delete(legacyBackup, recursive: true); } catch { }
            }

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
                WipeLocalGitRepo(workspacePath);
                return;
            }

            if (!string.IsNullOrWhiteSpace(siteTemplatePath) && Directory.Exists(siteTemplatePath))
            {
                CopyCleanTemplate(siteTemplatePath, workspacePath);
                WipeLocalGitRepo(workspacePath);
            }
        }
        catch { /* MainWindow.StartAsync still verifies & pulls baseline from Control Plane if needed */ }
    }

    /// <summary>
    /// Enforces AGENTS.md: "The server owns Git. Kiosks never run Git and never hold credentials."
    /// Strictly purges any .git repository from the workspace so git diff and git revert cannot be abused.
    /// </summary>
    public static void WipeLocalGitRepo(string workspacePath)
    {
        if (string.IsNullOrWhiteSpace(workspacePath) || !Directory.Exists(workspacePath)) return;
        try
        {
            var gitDir = Path.Combine(workspacePath, ".git");
            if (Directory.Exists(gitDir))
            {
                foreach (var file in Directory.EnumerateFiles(gitDir, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        File.SetAttributes(file, FileAttributes.Normal);
                        File.Delete(file);
                    }
                    catch { }
                }
                Directory.Delete(gitDir, recursive: true);
            }
        }
        catch { /* non-fatal cleanup */ }
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
