using System.IO;

namespace LabKiosk.Services;

/// <summary>Copies the participant's source files to .breach-backup/&lt;tag&gt; (plan section 5: flush before the breach).</summary>
public static class WorkspaceBackup
{
    private static readonly string[] Skip = { "node_modules", ".git", ".breach-backup" };

    public static void Snapshot(string root, string tag)
    {
        try { Copy(root, Path.Combine(root, ".breach-backup", tag)); } catch { /* a failed backup must never block the event */ }
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

    /// <summary>Plan section 7: use C:\BreachWorkspace, fall back to a folder next to the app if it is not writable.</summary>
    public static string ResolveWorkspace(string preferred)
    {
        try
        {
            Directory.CreateDirectory(preferred);
            var probe = Path.Combine(preferred, ".write-test");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return preferred;
        }
        catch
        {
            var fallback = Path.Combine(AppContext.BaseDirectory, "workspace");
            Directory.CreateDirectory(fallback);
            return fallback;
        }
    }
}
