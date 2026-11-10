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
        var path = Path.Combine(AppContext.BaseDirectory, "kiosk.json");
        var s = File.Exists(path)
            ? JsonSerializer.Deserialize<KioskSettings>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new()
            : new();

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
            s.SiteTemplatePath = Directory.Exists(st) ? st : Path.Combine(AppContext.BaseDirectory, "site");
        }

        return s;
    }

    public void Save()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "kiosk.json");
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch { }
    }
}
