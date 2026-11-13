using ControlPlane.Data;
using Microsoft.AspNetCore.SignalR;

namespace ControlPlane.Hubs;

/// <summary>Kiosks join their own group; admin pages join "admins"; projectors join "projectors".</summary>
public sealed class BreachHub(IConfiguration config, Database db) : Hub
{
    public static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> ActiveConnections = new();

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        if (ActiveConnections.TryRemove(Context.ConnectionId, out var pid))
        {
            _ = Clients.Group("admins").SendAsync(HubEvents.KioskUpdated, pid);
        }
        return base.OnDisconnectedAsync(exception);
    }

    public async Task RegisterKiosk(string participantId, string token)
    {
        if (string.IsNullOrWhiteSpace(token) || !db.ValidateDeskToken(token, participantId))
        {
            db.Log("HUB_KIOSK_AUTH_FAILED", participantId, payloadJson: $"{{\"connectionId\":\"{Context.ConnectionId}\"}}");
            Context.Abort();
            return;
        }
        Context.Items["participantId"] = participantId;
        ActiveConnections[Context.ConnectionId] = participantId;
        await Groups.AddToGroupAsync(Context.ConnectionId, $"p:{participantId}");
        await Groups.AddToGroupAsync(Context.ConnectionId, "arena");

        var canaryDeskId = config["RogueAgent:CanaryDeskId"] ?? "CANARY";
        var participantDesk = db.QueryScalar<string>("SELECT desk_id FROM desk_tokens WHERE participant_id = @p", ("@p", participantId));
        if (string.Equals(participantId, canaryDeskId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(participantDesk, canaryDeskId, StringComparison.OrdinalIgnoreCase))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, "canary");
        }
    }

    public Task JoinAdmin(string adminKey)
    {
        var expected = Environment.GetEnvironmentVariable("BREACH_ADMIN_KEY")
                    ?? Environment.GetEnvironmentVariable("ROGUE_ADMIN_KEY")
                    ?? config["Breach:AdminKey"];
        if (string.IsNullOrWhiteSpace(expected) || expected.Equals("CHANGE-ME-BEFORE-EVENT", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var keyFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BreachKiosk", "host", "admin.key");
                if (File.Exists(keyFile))
                {
                    expected = File.ReadAllText(keyFile).Trim();
                }
            }
            catch { }
        }
        expected = expected?.Trim() ?? "";
        adminKey = adminKey?.Trim() ?? "";

        var match = !string.IsNullOrEmpty(adminKey) && !string.IsNullOrEmpty(expected) &&
                    System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                        System.Text.Encoding.UTF8.GetBytes(adminKey),
                        System.Text.Encoding.UTF8.GetBytes(expected));

        if (!match)
        {
            var httpContext = Context.GetHttpContext();
            var remoteIp = httpContext?.Connection.RemoteIpAddress;
            var ipStr = remoteIp?.ToString();
            var allowedIps = config.GetSection("Breach:AdminAllowedIps").Get<string[]>() ?? Array.Empty<string>();
            var isAuthorized = remoteIp is null ||
                               System.Net.IPAddress.IsLoopback(remoteIp) ||
                               (remoteIp.IsIPv4MappedToIPv6 && System.Net.IPAddress.IsLoopback(remoteIp.MapToIPv4())) ||
                               ipStr == "::1" || ipStr == "127.0.0.1" || ipStr == "::ffff:127.0.0.1" ||
                               (ipStr != null && allowedIps.Contains(ipStr));

            var loopbackOnly = config.GetValue<bool>("Breach:AdminLoopbackOnly", true);
            if (loopbackOnly && !isAuthorized)
            {
                db.Log("HUB_ADMIN_DENIED_LAN", payloadJson: $"{{\"connectionId\":\"{Context.ConnectionId}\",\"ip\":\"{remoteIp}\"}}");
            }
            else
            {
                db.Log("HUB_ADMIN_AUTH_FAILED", payloadJson: $"{{\"connectionId\":\"{Context.ConnectionId}\"}}");
            }
            return Task.CompletedTask;
        }

        return Groups.AddToGroupAsync(Context.ConnectionId, "admins");
    }

    public async Task JoinProjector()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "projectors");
        await Groups.AddToGroupAsync(Context.ConnectionId, "arena");
    }

    /// <summary>Kiosk confirms it flushed a final snapshot after BREACH_IMMINENT.</summary>
    public Task AckImminent(int attackId)
    {
        if (Context.Items.TryGetValue("participantId", out var pidObj) && pidObj is string pid)
        {
            return Clients.Group("admins").SendAsync("ack", attackId, pid);
        }
        return Task.CompletedTask;
    }
}

public static class HubEvents
{
    public const string BreachImminent = "BREACH_IMMINENT";
    public const string AttackLanded = "ATTACK_LANDED";
    public const string AttackReset = "ATTACK_RESET";
    public const string Broadcast = "BROADCAST";
    public const string Paused = "PAUSED";
    public const string Resumed = "RESUMED";
    public const string Verdict = "VERDICT";
    public const string ScoreboardFrozen = "FROZEN";
    public const string ScoreboardUnfrozen = "UNFROZEN";
    public const string EventFrozen = "EVENT_FROZEN";
    public const string EventUnfrozen = "EVENT_UNFROZEN";
    public const string KioskUpdated = "KIOSK_UPDATED";
}
