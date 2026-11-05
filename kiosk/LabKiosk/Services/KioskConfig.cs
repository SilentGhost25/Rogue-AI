using System;
using System.Collections.Generic;

namespace LabKiosk.Services;

public sealed class KioskConfig
{
    public string ServerUrl { get; set; } = "http://127.0.0.1:5080";
    public string DeskId { get; set; } = "DESK-01";
    public string? DeskToken { get; set; }

    public static KioskConfig FromArgs(string[] args)
    {
        var m = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].StartsWith("--"))
            {
                m[args[i][2..]] = args[i + 1];
            }
        }

        var settings = KioskSettings.Load();

        return new KioskConfig
        {
            ServerUrl = m.GetValueOrDefault("server-url", string.IsNullOrWhiteSpace(settings.ServerUrl) ? "http://127.0.0.1:5080" : settings.ServerUrl),
            DeskId    = m.GetValueOrDefault("desk-id", !string.IsNullOrWhiteSpace(settings.DeskId) ? settings.DeskId : (!string.IsNullOrWhiteSpace(settings.KioskId) ? settings.KioskId : "DESK-01")),
            DeskToken = m.GetValueOrDefault("desk-token", settings.DeskToken)
        };
    }
}
