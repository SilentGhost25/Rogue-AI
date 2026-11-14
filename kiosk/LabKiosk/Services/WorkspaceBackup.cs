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
    /// Ensures the participant workspace is populated with a pristine baseline if missing, empty, or outdated.
    /// Extracts site.zip if bundled next to the executable, otherwise copies the clean site template directory.
    /// </summary>
    public static void EnsurePristineWorkspace(string workspacePath, string? siteTemplatePath)
    {
        try
        {
            Directory.CreateDirectory(workspacePath);
            var hasStaleLegacy = File.Exists(Path.Combine(workspacePath, "routes", "posts.js"))
                              || File.Exists(Path.Combine(workspacePath, "routes", "comments.js"))
                              || File.Exists(Path.Combine(workspacePath, "routes", "search.js"))
                              || File.Exists(Path.Combine(workspacePath, "public", "style.css"))
                              || !File.Exists(Path.Combine(workspacePath, "public", "aegis.css"))
                              || !File.Exists(Path.Combine(workspacePath, "routes", "traffic.js"));

            var isEmptyOrIncomplete = !Directory.EnumerateFileSystemEntries(workspacePath).Any()
                                   || !File.Exists(Path.Combine(workspacePath, "server.js"))
                                   || hasStaleLegacy;
            if (!isEmptyOrIncomplete) return;

            if (hasStaleLegacy)
            {
                foreach (var staleDir in new[] { "routes", "views", "lib", "public", "test" })
                {
                    var p = Path.Combine(workspacePath, staleDir);
                    if (Directory.Exists(p)) try { Directory.Delete(p, true); } catch { }
                }
            }

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
    /// Initializes a local git repository in the participant workspace so that AI tools (agy, opencode)
    /// and participants can natively use `git status`, `git diff`, and `git checkout`.
    /// </summary>
    public static void InitializeLocalGitRepo(string workspacePath, string deskId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(workspacePath) || !Directory.Exists(workspacePath)) return;
            var gitDir = Path.Combine(workspacePath, ".git");
            if (Directory.Exists(gitDir)) return;

            var gitignorePath = Path.Combine(workspacePath, ".gitignore");
            if (!File.Exists(gitignorePath))
            {
                File.WriteAllText(gitignorePath, "node_modules/\n.breach-backup/\n*.db\n*.db-wal\n*.db-shm\n*.sqlite*\n*.log\neval_*.db\n");
            }

            RunGit(workspacePath, "init");
            var safeDesk = (deskId ?? "Player").Replace("\"", "");
            RunGit(workspacePath, $"config user.name \"Participant ({safeDesk})\"");
            RunGit(workspacePath, "config user.email \"participant@rogue.internal\"");
            RunGit(workspacePath, "add -A");
            RunGit(workspacePath, "commit -m \"Baseline: AEGIS Municipal Core\"");
        }
        catch { /* git is optional on kiosk, never crashes startup */ }
    }

    /// <summary>
    /// Commits workspace changes into the local git repository (e.g. after an attack lands or resets).
    /// </summary>
    public static void CommitWorkspaceChanges(string workspacePath, string message)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(workspacePath) || !Directory.Exists(workspacePath)) return;
            var gitDir = Path.Combine(workspacePath, ".git");
            if (!Directory.Exists(gitDir)) return;

            var gitignorePath = Path.Combine(workspacePath, ".gitignore");
            if (!File.Exists(gitignorePath))
            {
                File.WriteAllText(gitignorePath, "node_modules/\n.breach-backup/\n*.db\n*.db-wal\n*.db-shm\n*.sqlite*\n*.log\neval_*.db\n");
            }

            RunGit(workspacePath, "add -A");
            var safeMsg = message.Replace("\"", "'");
            RunGit(workspacePath, $"commit -m \"{safeMsg}\" --allow-empty");
        }
        catch { /* best-effort commit */ }
    }

    private static void RunGit(string workingDir, string arguments)
    {
        try
        {
            using var proc = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = arguments,
                    WorkingDirectory = workingDir,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            proc.Start();
            if (!proc.WaitForExit(5000))
            {
                try { proc.Kill(); } catch { }
            }
        }
        catch { /* git binary might not be on system PATH */ }
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
