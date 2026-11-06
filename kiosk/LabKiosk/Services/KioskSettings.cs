using System.IO;
using System.Text.Json;

namespace LabKiosk.Services;

/// <summary>Read from kiosk.json next to the exe. The lab image provides one per laptop. Environment variables are expanded.</summary>
public sealed class KioskSettings
{
    public string ServerUrl { get; set; } = "http://192.168.1.10:5080";
    public string SiteUrl { get; set; } = "http://127.0.0.1:3000";
    public string WorkspacePath { get; set; } = @"C:\BreachWorkspace";
    public string DataPath { get; set; } = @"%LOCALAPPDATA%\BreachKiosk";
    public string SiteTemplatePath { get; set; } = "";          // default: site-template next to the exe
    public string KioskId { get; set; } = Environment.MachineName;
    public string DisplayName { get; set; } = Environment.MachineName;
    public bool Onboarded { get; set; } = false;
    public string GitHubHandle { get; set; } = "";
    public string GoogleAccount { get; set; } = "";

    public string NodePath { get; set; } = "node";
    public string AgyPath { get; set; } = @"%LOCALAPPDATA%\agy\bin\agy.exe";
    public string OpenCodePath { get; set; } = "opencode";
    public string OpenCodeModel { get; set; } = "";             // e.g. a free OpenCode Zen model id; empty keeps the default
    public int OpenCodePort { get; set; } = 4096;
    public int OpenCodeCap { get; set; } = 30;                   // starting cap: 30 prompts

    public static KioskSettings Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "kiosk.json");
        var s = File.Exists(path)
            ? JsonSerializer.Deserialize<KioskSettings>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new()
            : new();
        s.WorkspacePath = Environment.ExpandEnvironmentVariables(s.WorkspacePath);
        s.DataPath = Environment.ExpandEnvironmentVariables(s.DataPath);
        s.AgyPath = Environment.ExpandEnvironmentVariables(s.AgyPath);
        s.OpenCodePath = Environment.ExpandEnvironmentVariables(s.OpenCodePath);
        s.NodePath = Environment.ExpandEnvironmentVariables(s.NodePath);
        var bundledNode = Path.Combine(AppContext.BaseDirectory, "node", "node.exe");
        if ((s.NodePath == "node" || !File.Exists(s.NodePath)) && File.Exists(bundledNode)) s.NodePath = bundledNode;   // the installer ships Node
        if (string.IsNullOrWhiteSpace(s.SiteTemplatePath) || !Directory.Exists(s.SiteTemplatePath))
        {
            var st = Path.Combine(AppContext.BaseDirectory, "site-template");
            s.SiteTemplatePath = Directory.Exists(st) ? st : Path.Combine(AppContext.BaseDirectory, "site");
        }
        return s;
    }
}
