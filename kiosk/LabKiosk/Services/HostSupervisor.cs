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
    private readonly string _appDir = AppContext.BaseDirectory;
    private readonly string _dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BreachKiosk", "host");

    public string ServerUrl => "http://127.0.0.1:5080";
    public string EngineUrl => "http://127.0.0.1:8000";
    public string AdminKey { get; }

    public HostSupervisor()
    {
        Directory.CreateDirectory(_dataDir);
        AdminKey = LoadOrCreateKey();
    }

    private string LoadOrCreateKey()
    {
        var env = Environment.GetEnvironmentVariable("ROGUE_ADMIN_KEY");
        if (!string.IsNullOrWhiteSpace(env)) return env;
        var file = Path.Combine(_dataDir, "admin.key");
        if (File.Exists(file)) return File.ReadAllText(file).Trim();
        var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();
        File.WriteAllText(file, key);
        return key;
    }

    public async Task StartAsync(Action<string> status)
    {
        var workspaces = Path.Combine(_dataDir, "workspaces");
        Directory.CreateDirectory(workspaces);

        if (!await Healthy(EngineUrl + "/health"))
        {
            status("Starting the attack engine...");
            var python = File.Exists(Path.Combine(_appDir, "python", "python.exe")) ? Path.Combine(_appDir, "python", "python.exe") : "python";
            var psi = Hidden(python, "-m uvicorn breach_engine.api:app --host 0.0.0.0 --port 8000", Path.Combine(_appDir, "engine"));
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
            _children.Add(Process.Start(psi)!);
        }

        if (!await Healthy(ServerUrl + "/api/state"))
        {
            status("Starting the control plane...");
            var exe = Path.Combine(_appDir, "server", "ControlPlane.exe");
            var dll = Path.Combine(_appDir, "server", "ControlPlane.dll");
            var psi = File.Exists(exe) ? Hidden(exe, "", Path.Combine(_appDir, "server"))
                    : File.Exists(dll) ? Hidden("dotnet", $"\"{dll}\"", Path.Combine(_appDir, "server"))
                    : throw new FileNotFoundException("The control plane was not found (expected server\\ControlPlane.exe next to the app).");
            psi.Environment["ASPNETCORE_URLS"] = "http://0.0.0.0:5080";
            psi.Environment["Breach__AdminKey"] = AdminKey;
            psi.Environment["Breach__DatabasePath"] = Path.Combine(_dataDir, "breach.db");
            psi.Environment["Breach__WorkspacesPath"] = workspaces;
            psi.Environment["Breach__SiteTemplatePath"] = Path.Combine(_appDir, "site");
            psi.Environment["Breach__EngineUrl"] = EngineUrl;
            psi.Environment["RogueAgent__AttacksRoot"] = Path.Combine(_appDir, "attacks");
            psi.Environment["Breach__GitHubRepo"] = "Prince649294u83/Rogue-AI-competition";
            _children.Add(Process.Start(psi)!);
        }

        status("Waiting for services...");
        for (var i = 0; i < 80; i++)   // up to 40 s
        {
            if (await Healthy(EngineUrl + "/health") && await Healthy(ServerUrl + "/api/state")) { status("Ready."); return; }
            await Task.Delay(500);
        }
        throw new TimeoutException("The event services did not become healthy within 40 seconds.");
    }

    private static ProcessStartInfo Hidden(string exe, string args, string cwd)
        => new(exe, args) { WorkingDirectory = cwd, UseShellExecute = false, CreateNoWindow = true };

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
