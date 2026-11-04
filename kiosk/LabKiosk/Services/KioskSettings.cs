using System.Text.Json;

namespace LabKiosk.Services;

/// <summary>Read from kiosk.json next to the exe. The lab image provides one per laptop.</summary>
public sealed class KioskSettings
{
    public string ServerUrl { get; set; } = "http://192.168.1.10:5080";
    public string SiteUrl { get; set; } = "http://127.0.0.1:3000";
    public string WorkspacePath { get; set; } = @"C:\BreachWorkspace";
    public string KioskId { get; set; } = Environment.MachineName;
    public string DisplayName { get; set; } = Environment.MachineName;
    public string? DeskId { get; set; }
    public string? DeskToken { get; set; }

    public static KioskSettings Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "kiosk.json");
        return File.Exists(path)
            ? JsonSerializer.Deserialize<KioskSettings>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new()
            : new();
    }
}
