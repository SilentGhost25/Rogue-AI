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

    public async Task JoinAdmin(string? adminKey)
    {
        var httpContext = Context.GetHttpContext();
        var isSessionAuth = httpContext?.User.Identity?.IsAuthenticated == true && httpContext.User.IsInRole("admin");

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
        var providedKey = adminKey?.Trim() ?? "";

        var keyMatch = !string.IsNullOrEmpty(providedKey) && !string.IsNullOrEmpty(expected) &&
                       (providedKey.Equals(expected, StringComparison.Ordinal) ||
                        (providedKey.Length == expected.Length &&
                         System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                             System.Text.Encoding.UTF8.GetBytes(providedKey),
                             System.Text.Encoding.UTF8.GetBytes(expected))));

        var remoteIp = httpContext?.Connection.RemoteIpAddress;
        var ipStr = remoteIp?.ToString();
        var allowedIps = config.GetSection("Breach:AdminAllowedIps").Get<string[]>() ?? Array.Empty<string>();
        var isLoopbackOrAllowed = remoteIp is null ||
                           System.Net.IPAddress.IsLoopback(remoteIp) ||
                           (remoteIp.IsIPv4MappedToIPv6 && System.Net.IPAddress.IsLoopback(remoteIp.MapToIPv4())) ||
                           ipStr == "::1" || ipStr == "127.0.0.1" || ipStr == "::ffff:127.0.0.1" ||
                           (ipStr != null && allowedIps.Contains(ipStr));

        if (isSessionAuth || keyMatch || isLoopbackOrAllowed)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, "admins");
            db.Log("HUB_ADMIN_JOINED", payloadJson: $"{{\"connectionId\":\"{Context.ConnectionId}\",\"ip\":\"{remoteIp}\",\"sessionAuth\":{isSessionAuth.ToString().ToLowerInvariant()}}}");
            return;
        }

        db.Log("HUB_ADMIN_AUTH_FAILED", payloadJson: $"{{\"connectionId\":\"{Context.ConnectionId}\",\"ip\":\"{remoteIp}\"}}");
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
    public const string ParticipantRequest = "PARTICIPANT_REQUEST";
    public const string ParticipantAlert = "PARTICIPANT_ALERT";
    public const string RequestResolved = "REQUEST_RESOLVED";
}
