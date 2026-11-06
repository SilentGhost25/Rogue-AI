using System.IO;

namespace LabKiosk.Services;

public static class ExeResolver
{
    /// <summary>Finds a program by full path or on PATH (.exe first, then .cmd and .bat).</summary>
    public static string? Resolve(string nameOrPath)
    {
        if (Path.IsPathRooted(nameOrPath)) return File.Exists(nameOrPath) ? nameOrPath : null;
        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (var ext in new[] { ".exe", ".cmd", ".bat" })
            foreach (var dir in dirs)
            {
                var candidate = Path.Combine(dir.Trim('"'), nameOrPath + ext);
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
