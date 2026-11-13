using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;

namespace LabKiosk.Services;

/// <summary>
/// Organizer mode (`LabKiosk.exe --host`): starts the ASP.NET control plane and the Python engine as child processes,
/// waits until both answer, and stops them when the app closes. Anything already running is left alone.
/// </summary>
public sealed class HostSupervisor : IDisposable
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(2) };
    private readonly List<Process> _children = new();
    private readonly List<string> _errorHints = new();
    private readonly string _appDir = AppContext.BaseDirectory;
    private readonly string _dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BreachKiosk", "host");

    public string ServerUrl => "http://127.0.0.1:5080";
    public string EngineUrl => "http://127.0.0.1:8000";
    public string AdminKey { get; private set; }

    public HostSupervisor()
    {
        Directory.CreateDirectory(_dataDir);
        AdminKey = LoadOrCreateKey();
    }

    private string LoadOrCreateKey()
    {
        var env = Environment.GetEnvironmentVariable("BREACH_ADMIN_KEY")
               ?? Environment.GetEnvironmentVariable("ROGUE_ADMIN_KEY");
        if (!string.IsNullOrWhiteSpace(env) && env.Length >= 16) return env;
        var file = Path.Combine(_dataDir, "admin.key");
        if (File.Exists(file))
        {
            var saved = File.ReadAllText(file).Trim();
            if (saved.Length >= 16) return saved;
        }
        var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        File.WriteAllText(file, key);
        return key;
    }

    private string? FindRepoRoot()
    {
        var dir = new DirectoryInfo(_appDir);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "server", "ControlPlane", "ControlPlane.csproj")) &&
                File.Exists(Path.Combine(dir.FullName, "engine", "breach_engine", "api.py")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        return null;
    }

    private static string ResolvePythonExe(string appDir)
    {
        var bundled = Path.Combine(appDir, "python", "python.exe");
        if (File.Exists(bundled)) return bundled;
        var resolved = ExeResolver.Resolve("python");
        if (!string.IsNullOrWhiteSpace(resolved)) return resolved;
        return "python";
    }

    public async Task StartAsync(Action<string> status)
    {
        var repoRoot = FindRepoRoot();
        var engineDir = Directory.Exists(Path.Combine(_appDir, "engine"))
            ? Path.Combine(_appDir, "engine")
            : (repoRoot is not null ? Path.Combine(repoRoot, "engine") : Path.Combine(_appDir, "engine"));
        var attacksDir = Directory.Exists(Path.Combine(_appDir, "attacks"))
            ? Path.Combine(_appDir, "attacks")
            : (repoRoot is not null ? Path.Combine(repoRoot, "attacks") : Path.Combine(_appDir, "attacks"));
        var siteDir = Directory.Exists(Path.Combine(_appDir, "site"))
            ? Path.Combine(_appDir, "site")
            : (repoRoot is not null ? Path.Combine(repoRoot, "site") : Path.Combine(_appDir, "site"));
        var workspaces = repoRoot is not null
            ? Path.Combine(repoRoot, "server", "ControlPlane", "data", "workspaces")
            : Path.Combine(_dataDir, "workspaces");
        Directory.CreateDirectory(workspaces);

        Process? engineProc = null;
        Process? serverProc = null;

        if (!await Healthy(EngineUrl + "/health"))
        {
            status("Starting the attack engine...");
            var python = ResolvePythonExe(_appDir);
            var psi = Hidden(python, "-m uvicorn breach_engine.api:app --host 127.0.0.1 --port 8000", engineDir);
            var engineSecret = Environment.GetEnvironmentVariable("ROGUE_ENGINE_SECRET");
            if (string.IsNullOrWhiteSpace(engineSecret))
            {
                engineSecret = "RogueBreach_EngineSecret_" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Environment.MachineName + "_AegisInternal2026")));
                Environment.SetEnvironmentVariable("ROGUE_ENGINE_SECRET", engineSecret);
            }
            psi.Environment["ROGUE_ENGINE_SECRET"] = engineSecret;
            psi.Environment["PYTHONPATH"] = engineDir;
            psi.Environment["WORKSPACES_DIR"] = workspaces;
            psi.Environment["GIT_MIRROR_URL"] = Environment.GetEnvironmentVariable("GIT_MIRROR_URL") ?? "https://github.com/Prince649294u83/Rogue-AI-competition.git";
            var nodeExe = File.Exists(Path.Combine(_appDir, "nodejs", "node.exe"))
                ? Path.Combine(_appDir, "nodejs", "node.exe")
                : Path.Combine(_appDir, "node", "node.exe");
            if (File.Exists(nodeExe))
            {
                psi.Environment["NODE_BIN"] = nodeExe;
                var nodeDir = Path.GetDirectoryName(nodeExe)!;
                psi.Environment["PATH"] = nodeDir + Path.PathSeparator + (Environment.GetEnvironmentVariable("PATH") ?? "");
            }
            engineProc = StartTracked(psi, "Engine");
        }

        if (await Healthy(ServerUrl + "/api/state"))
        {
            if (!await KeyWorks(AdminKey) && await KeyWorks("LocalDevAdminKey_2026_Secure123"))
                AdminKey = "LocalDevAdminKey_2026_Secure123";
        }
        else
        {
            status("Starting the control plane...");

            // Safely kill any lingering/orphaned ControlPlane processes to prevent port 5080 address collisions
            try
            {
                foreach (var p in Process.GetProcessesByName("ControlPlane"))
                {
                    if (p.Id != Environment.ProcessId)
                    {
                        p.Kill(entireProcessTree: true);
                        p.WaitForExit(500);
                    }
                }
            }
            catch { }

            var serverDir = Path.Combine(_appDir, "server");
            var exe = Path.Combine(serverDir, "ControlPlane.exe");
            var dll = Path.Combine(serverDir, "ControlPlane.dll");
            var workDir = serverDir;

            if (!File.Exists(exe) && !File.Exists(dll) && repoRoot is not null)
            {
                workDir = Path.Combine(repoRoot, "server", "ControlPlane");
                foreach (var candidate in new[]
                {
                    Path.Combine(workDir, "bin", "Debug", "net9.0", "ControlPlane.exe"),
                    Path.Combine(workDir, "bin", "Release", "net9.0", "ControlPlane.exe"),
                    Path.Combine(workDir, "bin", "SelfContained", "ControlPlane.exe"),
                })
                {
                    if (File.Exists(candidate))
                    {
                        exe = candidate;
                        break;
                    }
                }
            }

            var psi = File.Exists(exe) ? Hidden(exe, "--urls http://0.0.0.0:5080", workDir)
                    : File.Exists(dll) ? Hidden("dotnet", $"\"{dll}\" --urls http://0.0.0.0:5080", workDir)
                    : throw new FileNotFoundException("The control plane was not found (expected server\\ControlPlane.exe).");

            var localDotnet = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "dotnet");
            if (Directory.Exists(localDotnet))
            {
                psi.Environment["DOTNET_ROOT"] = localDotnet;
                psi.Environment["PATH"] = localDotnet + Path.PathSeparator + (psi.Environment.TryGetValue("PATH", out var p) ? p : Environment.GetEnvironmentVariable("PATH") ?? "");
            }

            psi.Environment["ASPNETCORE_URLS"] = "http://0.0.0.0:5080";
            psi.Environment["BREACH_ADMIN_KEY"] = AdminKey;
            psi.Environment["Breach__AdminKey"] = AdminKey;
            psi.Environment["Breach__DatabasePath"] = repoRoot is not null
                ? Path.Combine(workDir, "data", "breach.db")
                : Path.Combine(_dataDir, "breach.db");
            psi.Environment["Breach__WorkspacesPath"] = workspaces;
            psi.Environment["Breach__SiteTemplatePath"] = siteDir;
            psi.Environment["Breach__EngineUrl"] = EngineUrl;
            psi.Environment["RogueAgent__AttacksRoot"] = attacksDir;
            psi.Environment["Breach__GitHubRepo"] = "Prince649294u83/Rogue-AI-competition";
            serverProc = StartTracked(psi, "ControlPlane");
        }

        status("Waiting for services...");
        for (var i = 0; i < 80; i++)   // up to 40 s
        {
            if (engineProc is { HasExited: true })
            {
                try { engineProc.WaitForExit(1000); } catch { }
                throw new InvalidOperationException($"Attack engine exited early (code {engineProc.ExitCode}). {string.Join(" | ", _errorHints)}");
            }
            if (serverProc is { HasExited: true })
            {
                try { serverProc.WaitForExit(1000); } catch { }
                var crashHint = ReadCrashLog();
                if (!string.IsNullOrWhiteSpace(crashHint))
                {
                    lock (_errorHints) { _errorHints.Insert(0, crashHint); }
                }
                throw new InvalidOperationException($"Control plane exited early (code {serverProc.ExitCode}). {string.Join(" | ", _errorHints)}");
            }

            if (await Healthy(EngineUrl + "/health") && await Healthy(ServerUrl + "/api/state")) { status("Ready."); return; }
            await Task.Delay(500);
        }
        throw new TimeoutException("The event services did not become healthy within 40 seconds.");
    }

    private static string? ReadCrashLog()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BreachKiosk", "host", "controlplane_crash.log"),
            Path.Combine(Path.GetTempPath(), "controlplane_crash.log"),
        };
        foreach (var path in candidates)
        {
            try
            {
                if (File.Exists(path))
                {
                    var lines = File.ReadAllLines(path).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
                    if (lines.Count > 0)
                    {
                        var header = string.Join(" | ", lines.Take(2).Select(l => l.Trim()));
                        if (header.Length > 200) header = header[..197] + "...";
                        return $"Crash: {header}";
                    }
                }
            }
            catch { }
        }
        return null;
    }

    private Process StartTracked(ProcessStartInfo psi, string label)
    {
        psi.RedirectStandardError = true;
        psi.RedirectStandardOutput = true;
        var proc = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start {label}.");

        void RecordHint(string? data)
        {
            if (string.IsNullOrWhiteSpace(data)) return;
            var t = data.Trim();
            if (t.Contains("crit:", StringComparison.OrdinalIgnoreCase) ||
                t.Contains("fail:", StringComparison.OrdinalIgnoreCase) ||
                t.Contains("warn:", StringComparison.OrdinalIgnoreCase) ||
                t.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                t.Contains("exception", StringComparison.OrdinalIgnoreCase) ||
                t.Contains("fatal", StringComparison.OrdinalIgnoreCase) ||
                t.Contains("unhandled", StringComparison.OrdinalIgnoreCase))
            {
                lock (_errorHints) { if (_errorHints.Count < 16) _errorHints.Add($"{label}: {t}"); }
            }
        }

        proc.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
                lock (_errorHints) { if (_errorHints.Count < 16) _errorHints.Add($"{label} [STDERR]: {e.Data.Trim()}"); }
        };
        proc.OutputDataReceived += (_, e) => RecordHint(e.Data);
        proc.BeginErrorReadLine();
        proc.BeginOutputReadLine();
        _children.Add(proc);
        return proc;
    }

    private static ProcessStartInfo Hidden(string exe, string args, string cwd)
        => new(exe, args) { WorkingDirectory = cwd, UseShellExecute = false, CreateNoWindow = true };

    private async Task<bool> KeyWorks(string key)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, ServerUrl + "/api/admin/attacks");
            req.Headers.Add("X-Admin-Key", key);
            using var res = await Http.SendAsync(req);
            return res.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    private static async Task<bool> Healthy(string url)
    {
        try { using var r = await Http.GetAsync(url); return r.IsSuccessStatusCode; }
        catch (HttpRequestException) { return false; }
        catch (TaskCanceledException) { return false; }
    }

    public void Dispose()
    {
        foreach (var p in _children) { try { p.Kill(entireProcessTree: true); } catch { } }
    }
}
