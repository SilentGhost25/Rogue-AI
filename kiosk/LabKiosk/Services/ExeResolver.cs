using System.IO;

namespace LabKiosk.Services;

public static class ExeResolver
{
    /// <summary>Finds a program by full path or on PATH (.exe first, then .cmd and .bat), with known fallback locations.</summary>
    public static string? Resolve(string nameOrPath)
    {
        if (Path.IsPathRooted(nameOrPath) && File.Exists(nameOrPath)) return nameOrPath;

        var baseName = Path.GetFileNameWithoutExtension(nameOrPath);

        // Check well-known tool locations for agy and gh
        if (baseName.Equals("agy", StringComparison.OrdinalIgnoreCase))
        {
            var u = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var l = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var candidates = new[]
            {
                Path.Combine(l, "agy", "bin", "agy.exe"),
                Path.Combine(u, ".gemini", "bin", "agy.exe"),
                Path.Combine(u, ".gemini", "antigravity-cli", "bin", "agy.exe")
            };
            foreach (var c in candidates)
                if (File.Exists(c)) return c;
        }
        else if (baseName.Equals("gh", StringComparison.OrdinalIgnoreCase))
        {
            var candidates = new[]
            {
                @"C:\Program Files\GitHub CLI\gh.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "GitHub CLI", "bin", "gh.exe")
            };
            foreach (var c in candidates)
                if (File.Exists(c)) return c;
        }

        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (var ext in new[] { ".exe", ".cmd", ".bat" })
            foreach (var dir in dirs)
            {
                var candidate = Path.Combine(dir.Trim('"'), baseName + ext);
                if (File.Exists(candidate)) return candidate;
            }
        return null;
    }

    /// <summary>.cmd and .bat shims cannot be started directly; wrap them in cmd.exe with the fixed arguments.</summary>
    public static (string Exe, string Args) ForLaunch(string resolvedPath, string args)
        => resolvedPath.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || resolvedPath.EndsWith(".bat", StringComparison.OrdinalIgnoreCase)
            ? (Environment.GetEnvironmentVariable("ComSpec") ?? @"C:\Windows\System32\cmd.exe", $"/c \"\"{resolvedPath}\" {args}\"")
            : (resolvedPath, args);
}
