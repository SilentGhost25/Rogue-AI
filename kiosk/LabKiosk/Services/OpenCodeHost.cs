using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace LabKiosk.Services;

/// <summary>
/// Runs `opencode serve` on loopback with a per-kiosk password. The terminal tab attaches to it, and this class counts user
/// prompts from the server's own session data (plan section 7, spike S2 confirms the endpoints on the pilot build).
/// </summary>
public sealed class OpenCodeHost : IDisposable
{
    private readonly string _exe, _workspace, _model;
    private readonly int _port;
    private readonly string _password = Guid.NewGuid().ToString("N");
    private readonly string _configPath;
    private readonly HttpClient _http;
    private Process? _serve;
    private CancellationTokenSource? _cts;
    private int _counted;
    private int _baseline = -1;

    public string Url => $"http://127.0.0.1:{_port}";
    public string Password => _password;
    public string ConfigPath => _configPath;

    /// <summary>Total user prompts seen so far.</summary>
    public event Action<int>? PromptCounted;
    public event Action<string>? Problem;

    public OpenCodeHost(string exe, string workspace, int port, string model, string dataPath)
    {
        _exe = exe; _workspace = workspace; _port = port; _model = model;
        Directory.CreateDirectory(dataPath);
        _configPath = Path.Combine(dataPath, "opencode.json");   // outside the workspace: participants cannot edit it
        _http = new HttpClient { BaseAddress = new Uri(Url), Timeout = TimeSpan.FromSeconds(5) };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"opencode:{_password}")));
    }

    public IReadOnlyDictionary<string, string?> Environment => new Dictionary<string, string?>
    {
        ["OPENCODE_SERVER_PASSWORD"] = _password,
        ["OPENCODE_CONFIG"] = _configPath,
        // Only the free provider is allowed; remove keys that could unlock paid ones.
        ["ANTHROPIC_API_KEY"] = null, ["OPENAI_API_KEY"] = null, ["GEMINI_API_KEY"] = null, ["GOOGLE_API_KEY"] = null,
    };

    public async Task StartAsync()
    {
        if (_serve is { HasExited: false }) return;
        WriteConfig();
        var resolved = ExeResolver.Resolve(_exe) ?? throw new FileNotFoundException("opencode was not found. Set OpenCodePath in kiosk.json.");
        var (exe, args) = ExeResolver.ForLaunch(resolved, $"serve --hostname 127.0.0.1 --port {_port}");
        var psi = new ProcessStartInfo(exe, args) { WorkingDirectory = _workspace, UseShellExecute = false, CreateNoWindow = true };
        foreach (var (k, v) in Environment) { if (v is null) psi.Environment.Remove(k); else psi.Environment[k] = v; }
        _serve = Process.Start(psi);

        for (var i = 0; i < 60; i++)   // wait up to 15 s for the port
        {
            if (_serve is null || _serve.HasExited) throw new InvalidOperationException("opencode serve exited at startup.");
            try { using var c = new TcpClient(); await c.ConnectAsync("127.0.0.1", _port); break; }
            catch (SocketException) { await Task.Delay(250); }
            if (i == 59) throw new TimeoutException("opencode serve did not open its port.");
        }

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        _ = PollAsync(_cts.Token);
    }

    private void WriteConfig()
    {
        var cfg = new Dictionary<string, object?>
        {
            ["$schema"] = "https://opencode.ai/config.json",
            ["autoupdate"] = false,
            ["share"] = "disabled",
            ["enabled_providers"] = new[] { "opencode" },
        };
        if (!string.IsNullOrWhiteSpace(_model)) cfg["model"] = _model;
        File.WriteAllText(_configPath, JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true }));
    }

    private async Task PollAsync(CancellationToken ct)
    {
        var failures = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var total = await CountUserMessagesAsync(ct);
                failures = 0;
                if (_baseline < 0)
                {
                    _baseline = total;
                    _counted = 0;
                    PromptCounted?.Invoke(0);
                }
                else
                {
                    var sessionPrompts = Math.Max(0, total - _baseline);
                    if (sessionPrompts > _counted)
                    {
                        _counted = sessionPrompts;
                        PromptCounted?.Invoke(sessionPrompts);
                    }
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                if (!ct.IsCancellationRequested && ++failures == 10) Problem?.Invoke("Cannot read OpenCode prompt counts: " + ex.Message);
            }
            try { await Task.Delay(2000, ct); } catch (OperationCanceledException) { return; }
        }
    }

    private async Task<int> CountUserMessagesAsync(CancellationToken ct)
    {
        using var sessions = JsonDocument.Parse(await _http.GetStringAsync("/session", ct));
        var total = 0;
        foreach (var s in sessions.RootElement.EnumerateArray())
        {
            if (!s.TryGetProperty("id", out var idEl) || idEl.GetString() is not { } id) continue;
            using var msgs = JsonDocument.Parse(await _http.GetStringAsync($"/session/{Uri.EscapeDataString(id)}/message", ct));
            foreach (var m in msgs.RootElement.EnumerateArray())
                if (m.TryGetProperty("info", out var info) && info.TryGetProperty("role", out var role) && role.GetString() == "user") total++;
        }
        return total;
    }

    public static async Task<string?> EnsureInstalledAsync(string dataPath, string configuredPath = "opencode")
    {
        var existing = ExeResolver.Resolve(configuredPath);
        if (existing is not null) return existing;

        try
        {
            var destDir = Path.Combine(dataPath, "opencode");
            Directory.CreateDirectory(destDir);
            var exePath = Path.Combine(destDir, "opencode.exe");
            if (File.Exists(exePath)) return exePath;

            var nodeCandidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "nodejs", "node.exe"),
                Path.Combine(AppContext.BaseDirectory, "node", "node.exe"),
                ExeResolver.Resolve("node") ?? "node"
            };
            var nodeExe = nodeCandidates.FirstOrDefault(File.Exists);
            if (nodeExe is null) return null;

            var js = """
const https = require("https"), fs = require("fs"), path = require("path"), zlib = require("zlib");
const get = (u) => new Promise((res, rej) => https.get(u, { headers: { "User-Agent": "rogue-ai-kiosk" } }, (r) => {
  if (r.statusCode >= 300 && r.statusCode < 400 && r.headers.location) return get(r.headers.location).then(res, rej);
  if (r.statusCode !== 200) return rej(new Error("HTTP " + r.statusCode));
  const chunks = []; r.on("data", c => chunks.push(c)); r.on("end", () => res(Buffer.concat(chunks)));
}).on("error", rej));
(async () => {
  const meta = JSON.parse((await get("https://registry.npmjs.org/opencode-windows-x64/latest")).toString());
  const tgz = await get(meta.dist.tarball);
  const tar = zlib.gunzipSync(tgz);
  let off = 0;
  while (off + 512 <= tar.length) {
    const hdr = tar.subarray(off, off + 512); off += 512;
    if (hdr.every(b => b === 0)) break;
    const name = hdr.subarray(0, 100).toString("utf8").replace(/\0.*$/, "");
    const size = parseInt(hdr.subarray(124, 136).toString("utf8").replace(/\0.*$/, "").trim() || "0", 8);
    if (name.endsWith("bin/opencode.exe") && size > 0) {
      fs.writeFileSync(path.join(process.argv[1], "opencode.exe"), tar.subarray(off, off + size));
      return;
    }
    off += Math.ceil(size / 512) * 512;
  }
  throw new Error("bin/opencode.exe not found");
})();
""";
            var tmpJs = Path.Combine(destDir, "_fetch_opencode.js");
            await File.WriteAllTextAsync(tmpJs, js);
            var psi = new ProcessStartInfo(nodeExe)
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add(tmpJs);
            psi.ArgumentList.Add(destDir);
            using var p = Process.Start(psi);
            if (p is not null)
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                await p.WaitForExitAsync(cts.Token);
            }
            try { File.Delete(tmpJs); } catch { }
            if (File.Exists(exePath)) return exePath;
        }
        catch { }
        return ExeResolver.Resolve(configuredPath);
    }

    public void Stop()
    {
        _cts?.Cancel();
        try { _serve?.Kill(entireProcessTree: true); } catch { }
    }

    public void Dispose() { Stop(); _http.Dispose(); }
}
