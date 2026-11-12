using System.IO;
using System.Text.Json;

namespace LabKiosk.Services;

/// <summary>Read from kiosk.json next to the exe. The lab image provides one per laptop.</summary>
public sealed class KioskSettings
{
    public string ServerUrl { get; set; } = "http://127.0.0.1:5080";
    public string SiteUrl { get; set; } = "http://127.0.0.1:3000";
    public string WorkspacePath { get; set; } = @"C:\BreachWorkspace";
    public string KioskId { get; set; } = Environment.MachineName;
    public string DisplayName { get; set; } = Environment.MachineName;
    public string? DeskId { get; set; }
    public string? DeskToken { get; set; }
    public string? Password { get; set; }
    public bool Onboarded { get; set; } = false;
    public string GitHubHandle { get; set; } = "";
    public string GoogleAccount { get; set; } = "";
    public string DataPath { get; set; } = @"%LOCALAPPDATA%\BreachKiosk";
    public string SiteTemplatePath { get; set; } = "";
    public string NodePath { get; set; } = "node";
    public string AgyPath { get; set; } = @"%LOCALAPPDATA%\agy\bin\agy.exe";
    public string OpenCodePath { get; set; } = "opencode";
    public string OpenCodeModel { get; set; } = "";
    public int OpenCodePort { get; set; } = 4096;
    public int OpenCodeCap { get; set; } = 30;

    public static KioskSettings Load()
    {
        var basePath = Path.Combine(AppContext.BaseDirectory, "kiosk.json");
        var userPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BreachKiosk", "kiosk.json");

        KioskSettings s = new();
        if (File.Exists(basePath))
        {
            try
            {
                s = JsonSerializer.Deserialize<KioskSettings>(File.ReadAllText(basePath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
            }
            catch { }
        }

        // Apply user overlay overrides if present
        if (File.Exists(userPath))
        {
            try
            {
                var userS = JsonSerializer.Deserialize<KioskSettings>(File.ReadAllText(userPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (userS != null)
                {
                    if (!string.IsNullOrWhiteSpace(userS.DisplayName)) s.DisplayName = userS.DisplayName;
                    if (!string.IsNullOrWhiteSpace(userS.KioskId)) s.KioskId = userS.KioskId;
                    if (!string.IsNullOrWhiteSpace(userS.DeskId)) s.DeskId = userS.DeskId;
                    if (!string.IsNullOrWhiteSpace(userS.DeskToken)) s.DeskToken = userS.DeskToken;
                    if (!string.IsNullOrWhiteSpace(userS.Password)) s.Password = userS.Password;
                    if (userS.Onboarded) s.Onboarded = true;
                    if (!string.IsNullOrWhiteSpace(userS.GitHubHandle)) s.GitHubHandle = userS.GitHubHandle;
                    if (!string.IsNullOrWhiteSpace(userS.GoogleAccount)) s.GoogleAccount = userS.GoogleAccount;
                    if (!string.IsNullOrWhiteSpace(userS.ServerUrl) && !userS.ServerUrl.Equals("AUTO", StringComparison.OrdinalIgnoreCase)) s.ServerUrl = userS.ServerUrl;
                    if (!string.IsNullOrWhiteSpace(userS.WorkspacePath)) s.WorkspacePath = userS.WorkspacePath;
                    if (userS.OpenCodeCap > 0) s.OpenCodeCap = userS.OpenCodeCap;
                }
            }
            catch { }
        }

        s.WorkspacePath = Environment.ExpandEnvironmentVariables(s.WorkspacePath ?? @"C:\BreachWorkspace");
        s.DataPath = Environment.ExpandEnvironmentVariables(s.DataPath ?? @"%LOCALAPPDATA%\BreachKiosk");
        s.AgyPath = Environment.ExpandEnvironmentVariables(s.AgyPath ?? @"%LOCALAPPDATA%\agy\bin\agy.exe");
        s.OpenCodePath = Environment.ExpandEnvironmentVariables(s.OpenCodePath ?? "opencode");
        s.NodePath = Environment.ExpandEnvironmentVariables(s.NodePath ?? "node");

        var bundledNode = Path.Combine(AppContext.BaseDirectory, "node", "node.exe");
        var bundledNodeJs = Path.Combine(AppContext.BaseDirectory, "nodejs", "node.exe");
        if (s.NodePath == "node" || !File.Exists(s.NodePath))
        {
            if (File.Exists(bundledNode)) s.NodePath = bundledNode;
            else if (File.Exists(bundledNodeJs)) s.NodePath = bundledNodeJs;
        }

        if (string.IsNullOrWhiteSpace(s.SiteTemplatePath) || !Directory.Exists(s.SiteTemplatePath))
        {
            var st = Path.Combine(AppContext.BaseDirectory, "site-template");
            var appSite = Path.Combine(AppContext.BaseDirectory, "site");
            if (Directory.Exists(st))
                s.SiteTemplatePath = st;
            else if (Directory.Exists(appSite))
                s.SiteTemplatePath = appSite;
            else
            {
                var probe = new DirectoryInfo(AppContext.BaseDirectory);
                for (var i = 0; i < 6 && probe is not null; i++, probe = probe.Parent)
                {
                    var candidate = Path.Combine(probe.FullName, "site");
                    if (File.Exists(Path.Combine(candidate, "server.js")))
                    {
                        s.SiteTemplatePath = candidate;
                        break;
                    }
                }
                if (string.IsNullOrWhiteSpace(s.SiteTemplatePath))
                    s.SiteTemplatePath = appSite;
            }
        }

        s.ServerUrl = NormalizeServerUrl(s.ServerUrl);
        s.SiteUrl = NormalizeSiteUrl(s.SiteUrl);

        return s;
    }

    public static string NormalizeSiteUrl(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "http://127.0.0.1:3000";
        var clean = raw.Trim().TrimEnd('/');
        if (!clean.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !clean.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            clean = "http://" + clean;
        }
        return clean;
    }

    public static string NormalizeServerUrl(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "http://127.0.0.1:5080";
        var clean = raw.Trim().TrimEnd('/');
        if (clean.Equals("AUTO", StringComparison.OrdinalIgnoreCase)) return "http://127.0.0.1:5080";
        if (!clean.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !clean.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            clean = clean.Contains(':') ? "http://" + clean : "http://" + clean + ":5080";
        }
        return clean;
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });

        // Always save to user overlay (%LOCALAPPDATA%\BreachKiosk\kiosk.json) so standard users never fail
        try
        {
            var userDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BreachKiosk");
            Directory.CreateDirectory(userDir);
            var userPath = Path.Combine(userDir, "kiosk.json");
            File.WriteAllText(userPath, json);
        }
        catch { }

        // Also attempt best-effort write to base app directory
        try
        {
            var basePath = Path.Combine(AppContext.BaseDirectory, "kiosk.json");
            File.WriteAllText(basePath, json);
        }
        catch { }
    }
}
